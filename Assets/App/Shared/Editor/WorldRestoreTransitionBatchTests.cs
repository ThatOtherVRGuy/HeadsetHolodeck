using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Holodeck.Save;
using Holodeck.Direct;
using SpeechIntent;
using UnityEngine;
using UnityEngine.Events;
using WorldLabs.Runtime;

namespace HeadsetHolodeck.EditorTests
{
    /// <summary>Offline fixtures. The async runner must await this entry on Unity's context.</summary>
    public static class WorldRestoreTransitionBatchTests
    {
        public static async Task RunAllAsync()
        {
            await TestRapidRestoreAndLateCompletion();
            await TestCancelPlacementDisableAndRetry();
            await TestFailedRestoreAndRetry();
            await TestCacheCompletionCannotMutateNextConfig();
            await TestPanoCancellationAndLateCompletion();
            TestBrowserPanoramaClearsWithWorldLifetime();
            TestReentrantConfigCreationCannotReplaceNewActiveConfig();
            await TestReentrantRestoreCancellation();
            TestAutoSaveDisableDropsMissedWorldBinding();
            Debug.Log("[WorldRestoreTransitionBatchTests] All 9 tests passed.");
        }

        static async Task TestRapidRestoreAndLateCompletion()
        {
            using var fixture = new Fixture();
            WorldConfig a = fixture.Config("A"), b = fixture.Config("B");
            Task first = fixture.Restorer.RestoreAsync(a);
            RestoreRequest requestA = fixture.Restorer.Requests[0];
            Assert(fixture.AutoSave.ActiveConfig == null, "Pending A must not become ActiveConfig.");
            Task second = fixture.Restorer.RestoreAsync(b);
            RestoreRequest requestB = fixture.Restorer.Requests[1];
            requestB.Completion.SetResult(true);
            await second;
            Assert(ReferenceEquals(fixture.AutoSave.ActiveConfig, b), "B should be active after its exact load succeeds.");
            requestA.Completion.SetResult(true);
            await first;
            await Task.Yield();
            Assert(ReferenceEquals(fixture.AutoSave.ActiveConfig, b), "Late A completion must not change B's ActiveConfig.");
            Assert(!requestA.Operation.IsCurrent && requestB.Operation.IsCurrent, "Only B retains the world lifetime.");
            Assert(fixture.Completed == 1 && fixture.Errors == 0, "Superseded A must not emit completion or failure.");
            Debug.Log("[WorldRestoreTransitionBatchTests] PASS rapid A/B and late completion");
        }

        static async Task TestCancelPlacementDisableAndRetry()
        {
            using var fixture = new Fixture();
            Task pending = fixture.Restorer.RestoreAsync(fixture.Config("Placement"));
            RestoreRequest request = fixture.Restorer.Requests[0];
            GameObject root = request.CreatePlacementRoot();
            fixture.Restorer.gameObject.SetActive(false);
            await pending;
            Assert(!request.Operation.IsCurrent && root == null, "Disable must cancel placement and destroy its owned root.");
            Assert(fixture.AutoSave.ActiveConfig == null, "Disable must clear the pending config.");
            request.Completion.SetResult(true);
            await Task.Yield();
            fixture.Restorer.gameObject.SetActive(true);
            WorldConfig retry = fixture.Config("RetryAfterDisable");
            Task retryTask = fixture.Restorer.RestoreAsync(retry);
            fixture.Restorer.Requests[1].Completion.SetResult(true);
            await retryTask;
            Assert(ReferenceEquals(fixture.AutoSave.ActiveConfig, retry), "A retry after re-enable must succeed.");
            fixture.Manager.UnloadAllWorlds();
            Assert(fixture.AutoSave.ActiveConfig == null, "Explicit unload must clear the active restored config.");
            Debug.Log("[WorldRestoreTransitionBatchTests] PASS placement cancellation, disable/unload, retry");
        }

        static async Task TestFailedRestoreAndRetry()
        {
            using var fixture = new Fixture();
            Task failed = fixture.Restorer.RestoreAsync(fixture.Config("Broken"));
            fixture.Restorer.Requests[0].Completion.SetResult(false);
            await failed;
            Assert(fixture.AutoSave.ActiveConfig == null && fixture.Errors == 1, "Failed restore must leave no active config and report one error.");
            WorldConfig retry = fixture.Config("Fixed");
            Task pending = fixture.Restorer.RestoreAsync(retry);
            fixture.Restorer.Requests[1].Completion.SetResult(true);
            await pending;
            Assert(ReferenceEquals(fixture.AutoSave.ActiveConfig, retry) && fixture.Completed == 1, "Retry must not be blocked by failed restore state.");
            Debug.Log("[WorldRestoreTransitionBatchTests] PASS failed restore and retry");
        }

        static async Task TestCacheCompletionCannotMutateNextConfig()
        {
            using var fixture = new Fixture();
            WorldConfig a = fixture.Config("CacheA"), b = fixture.Config("CacheB");
            fixture.AutoSave.ActiveConfig = a;
            Task cache = (Task)typeof(WorldConfigAutoSave).GetMethod("CacheSplatBytesAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(fixture.AutoSave, new object[] { new byte[] { 1, 2, 3 } });
            Assert(fixture.AutoSave.WritePath != null, "Fixture must reach the actual asynchronous cache-write boundary.");
            fixture.AutoSave.ActiveConfig = b;
            fixture.AutoSave.WriteCompletion.SetResult(true);
            await cache;
            Assert(ReferenceEquals(fixture.AutoSave.ActiveConfig, b), "Late cache write must not change ActiveConfig.");
            Assert(string.IsNullOrEmpty(b.world_source.cached_splat), "A's cache result must never be assigned to B.");
            Assert(string.IsNullOrEmpty(a.world_source.cached_splat), "Canceled cache metadata must not be published after its config lifetime.");
            Assert(!File.Exists(fixture.AutoSave.WritePath), "The cache test seam must not write content outside its fixture.");
            Debug.Log("[WorldRestoreTransitionBatchTests] PASS asynchronous cache destination identity");
        }

        static async Task TestPanoCancellationAndLateCompletion()
        {
            var go = new GameObject("OfflinePanoFixture");
            var loader = go.AddComponent<ControlledPanoLoader>();
            var operation = WorldTransitionOperation.CreateStandalone();
            var next = WorldTransitionOperation.CreateStandalone();
            loader.thumbnailSkybox = go.AddComponent<ThumbnailSkyboxController>();
            try
            {
                Task<bool> pending = loader.PreloadAndWaitAsync("https://fixture.invalid/panorama.jpg", operation);
                Assert(loader.ReadCalls == 1, "Panorama fixture must enter its controlled read boundary.");
                operation.Cancel();
                Assert(!await pending, "Cancellation must settle before the ignored read finishes.");
                Task<bool> replacement = loader.PreloadAndWaitAsync("https://fixture.invalid/B.jpg", next);
                var pixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
                byte[] validImage = pixel.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(pixel);
                loader.Reads[1].SetResult(validImage);
                Assert(await replacement && loader.thumbnailSkybox.HasStoredTexture, "B panorama should store successfully.");
                loader.Completion.SetResult(new byte[] { 0 });
                await Task.Yield();
                Assert(loader.BytesPublished == 1 && loader.thumbnailSkybox.HasStoredTexture,
                    "Late A must neither publish cache bytes nor clear B's stored panorama.");
                next.Cancel();
                Assert(!loader.thumbnailSkybox.HasStoredTexture, "Canceling B must clear its panorama immediately.");
                Debug.Log("[WorldRestoreTransitionBatchTests] PASS panorama A/B, download cancellation and late completion");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        static void TestBrowserPanoramaClearsWithWorldLifetime()
        {
            var root = new GameObject("OfflineBrowserPanoFixture");
            var manager = root.AddComponent<WorldLabsWorldManager>();
            var skybox = root.AddComponent<ThumbnailSkyboxController>();
            try
            {
                manager.BeginTransition();
                // WorldBrowserController publishes through a UnityEvent<Texture2D>; it
                // cannot reference app code, so Store/Show must bind the world lifetime.
                skybox.Store(new Texture2D(1, 1));
                Assert(skybox.HasStoredTexture, "Browser panorama must initially be stored.");
                manager.BeginTransition();
                Assert(!skybox.HasStoredTexture, "Superseding the browser operation must clear its panorama.");
                Debug.Log("[WorldRestoreTransitionBatchTests] PASS browser panorama world ownership");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static void TestReentrantConfigCreationCannotReplaceNewActiveConfig()
        {
            using var fixture = new Fixture();
            WorldConfig replacement = fixture.Config("ReentrantB");
            WorldTransitionOperation first = fixture.Manager.BeginTransition();
            void StartReplacement()
            {
                fixture.Manager.BeginTransition();
                fixture.AutoSave.ActiveConfig = replacement;
            }
            fixture.Store.OnConfigsChanged += StartReplacement;
            try
            {
                typeof(WorldConfigAutoSave).GetMethod("OnWorldLoaded", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(fixture.AutoSave, new object[] { "ReentrantA", null });
                Assert(!first.IsCurrent && ReferenceEquals(fixture.AutoSave.ActiveConfig, replacement),
                    "Creating A's config must not overwrite B if a save listener starts B before CreateConfig returns.");
                Debug.Log("[WorldRestoreTransitionBatchTests] PASS reentrant config creation");
            }
            finally { fixture.Store.OnConfigsChanged -= StartReplacement; }
        }

        static async Task TestReentrantRestoreCancellation()
        {
            using var fixture = new Fixture();
            Task original = fixture.Restorer.RestoreAsync(fixture.Config("Original"));
            RestoreRequest originalRequest = fixture.Restorer.Requests[0];
            WorldConfig replacement = fixture.Config("CallbackB");
            Task callbackRestore = null;
            void StartReplacement(WorldTransitionOperation canceled)
            {
                if (callbackRestore != null || !ReferenceEquals(canceled, originalRequest.Operation)) return;
                // Set a sentinel before invoking RestoreAsync because BeginTransition can
                // dispatch another cancellation notification for the previous identity.
                callbackRestore = Task.CompletedTask;
                callbackRestore = fixture.Restorer.RestoreAsync(replacement);
            }
            fixture.Manager.OnTransitionCancelled += StartReplacement;
            try
            {
                await fixture.Restorer.RestoreAsync(fixture.Config("InterruptedA"));
                Assert(fixture.Restorer.Requests.Count == 2,
                    "A cancellation callback's newer B request must supersede the outer A before A starts acquisition.");
                fixture.Restorer.Requests[1].Completion.SetResult(true);
                await callbackRestore;
                await original;
                originalRequest.Completion.SetResult(false);
                Assert(ReferenceEquals(fixture.AutoSave.ActiveConfig, replacement) && fixture.Errors == 0,
                    "Reentrant cancellation must retain B's config and suppress stale errors.");
                Debug.Log("[WorldRestoreTransitionBatchTests] PASS reentrant restore cancellation");
            }
            finally { fixture.Manager.OnTransitionCancelled -= StartReplacement; }
        }

        static void TestAutoSaveDisableDropsMissedWorldBinding()
        {
            using var fixture = new Fixture();
            WorldConfig first = fixture.Config("DisabledAutoSaveA");
            WorldTransitionOperation operation = fixture.Manager.BeginTransition();
            fixture.AutoSave.BeginRestore(operation, first);
            fixture.AutoSave.CommitRestore(operation);
            typeof(WorldConfigAutoSave).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(fixture.AutoSave, null);
            fixture.AutoSave.CommitRestore(operation);
            Assert(fixture.AutoSave.ActiveConfig == null,
                "Disabling autosave must clear both active and pending config bindings; a late commit cannot restore A.");
            fixture.Manager.BeginTransition();
            typeof(WorldConfigAutoSave).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(fixture.AutoSave, null);
            Assert(fixture.AutoSave.ActiveConfig == null && !fixture.AutoSave.SaveActiveConfig(),
                "Re-enabled autosave must not retain A after missing another world transition.");
            Debug.Log("[WorldRestoreTransitionBatchTests] PASS autosave disable/re-enable clears stale config");
        }

        static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        sealed class Fixture : IDisposable
        {
            readonly List<GameObject> roots = new();
            readonly string directory = Path.Combine(Path.GetTempPath(), "HolodeckRestoreTransition_" + Guid.NewGuid().ToString("N"));
            public readonly WorldLabsWorldManager Manager;
            public readonly ControlledWorldRestorer Restorer;
            public readonly ControlledWorldAutoSave AutoSave;
            public readonly WorldConfigStore Store;
            public int Completed, Errors;

            public Fixture()
            {
                Directory.CreateDirectory(directory);
                Store = WorldConfigStore.CreateForTesting(directory);
                roots.Add(Store.gameObject);
                Manager = Add<ControlledWorldTransitionManager>("OfflineWorldManager");
                Manager.unloadUnusedAssetsBeforeLoad = false;
                Manager.resetSplatRenderSystemBeforeLoad = false;
                Manager.loadCleanupFrameDelay = 0;
                // Keep autosave inactive so its Start cannot reconcile with an external provider.
                GameObject autoSaveRoot = new GameObject("OfflineAutoSave");
                autoSaveRoot.SetActive(false);
                roots.Add(autoSaveRoot);
                AutoSave = autoSaveRoot.AddComponent<ControlledWorldAutoSave>();
                AutoSave.worldManager = Manager;
                AutoSave.worldConfigStore = Store;
                typeof(WorldConfigAutoSave).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(AutoSave, null);
                Restorer = Add<ControlledWorldRestorer>("OfflineRestorer");
                Restorer.worldManager = Manager;
                Restorer.worldConfigAutoSave = AutoSave;
                Restorer.worldConfigStore = Store;
                Restorer.onRestoreComplete = new UnityEvent();
                Restorer.onRestoreComplete.AddListener(() => ++Completed);
                Restorer.onRestoreError = new StringEvent();
                Restorer.onRestoreError.AddListener(_ => ++Errors);
            }

            T Add<T>(string name) where T : Component
            {
                var root = new GameObject(name);
                roots.Add(root);
                return root.AddComponent<T>();
            }
            public WorldConfig Config(string name) => Store.CreateConfig(new WorldSourceData { type = "fixture", world_id = name }, name, null);
            public void Dispose()
            {
                Restorer.CancelRestore();
                typeof(WorldConfigAutoSave).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(AutoSave, null);
                for (int i = roots.Count - 1; i >= 0; --i)
                    if (roots[i] != null) UnityEngine.Object.DestroyImmediate(roots[i]);
                Directory.Delete(directory, true);
            }
        }
    }

    public sealed class RestoreRequest
    {
        public readonly WorldTransitionOperation Operation;
        public readonly TaskCompletionSource<bool> Completion = new();
        public RestoreRequest(WorldTransitionOperation operation) => Operation = operation;
        public GameObject CreatePlacementRoot()
        {
            var root = new GameObject("FixturePendingPlacement");
            Operation.Track(root);
            return root;
        }
    }

    [ExecuteAlways]
    public sealed class ControlledWorldRestorer : WorldConfigRestorer
    {
        public readonly List<RestoreRequest> Requests = new();
        protected override async Task<bool> LoadWorldForRestoreAsync(WorldConfig config, WorldTransitionOperation operation)
        {
            var request = new RestoreRequest(operation);
            Requests.Add(request);
            // Intentionally ignores cancellation while waiting, like a non-cancelable importer.
            bool result = await request.Completion.Task;
            operation.ThrowIfStale();
            return result;
        }
    }

    public sealed class ControlledWorldAutoSave : WorldConfigAutoSave
    {
        public readonly TaskCompletionSource<bool> WriteCompletion = new();
        public string WritePath;
        protected override Task WriteCacheBytesAsync(string path, byte[] bytes)
        {
            WritePath = path;
            return WriteCompletion.Task;
        }
    }

    [ExecuteAlways]
    public sealed class ControlledPanoLoader : LocalRemotePanoLoader
    {
        public readonly List<TaskCompletionSource<byte[]>> Reads = new();
        public TaskCompletionSource<byte[]> Completion => Reads[0];
        public int ReadCalls, BytesPublished;
        void OnEnable() => OnPanoBytesLoaded += _ => ++BytesPublished;
        protected override Task<byte[]> ReadImageBytesAsync(string path, CancellationToken token)
        {
            ++ReadCalls;
            var request = new TaskCompletionSource<byte[]>();
            Reads.Add(request);
            return request.Task;
        }
    }
}
