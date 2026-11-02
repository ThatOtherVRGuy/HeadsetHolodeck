using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GaussianSplatting.Runtime;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldLabs.API;
using WorldLabs.Runtime;

namespace HeadsetHolodeck.EditorTests
{
    /// <summary>Controlled I/O around the production load pipeline; never contacts a provider or uploads splats.</summary>
    [ExecuteAlways]
    public sealed class ControlledWorldTransitionManager : WorldLabsWorldManager
    {
        public readonly Queue<Task<byte[]>> Downloads = new();
        public readonly Queue<Task<RuntimeSplatData>> Processing = new();
        public readonly List<CancellationToken> DownloadTokens = new();
        public Action<GaussianSplatRenderer> RendererDataLoaded;
        public int ProcessingCalls;

        protected override Task<byte[]> DownloadWorldBytesAsync(string url, CancellationToken token)
        {
            DownloadTokens.Add(token);
            if (!url.StartsWith("fixture://", StringComparison.Ordinal))
                throw new InvalidOperationException("Transition tests only accept fixture URLs.");
            return Downloads.Count > 0 ? Downloads.Dequeue() : Task.FromResult(new byte[] { 1 });
        }

        protected override Task<RuntimeSplatData> ProcessWorldDataAsync(byte[] bytes)
        {
            ProcessingCalls++;
            return Processing.Count > 0 ? Processing.Dequeue() : Task.FromResult(new RuntimeSplatData());
        }

        protected override void LoadRendererData(GaussianSplatRenderer renderer, RuntimeSplatData data)
        {
            // Production still activates/registers the GameObject; disable only rendering so
            // this state-machine fixture never initializes GPU sorting in -nographics mode.
            renderer.enabled = false;
            RendererDataLoaded?.Invoke(renderer);
        }
    }

    public static class WorldTransitionBatchTests
    {
        sealed class Fixture : IDisposable
        {
            public readonly GameObject Root;
            public readonly ControlledWorldTransitionManager Manager;
            public readonly List<string> Loaded = new();
            public readonly List<string> Failed = new();
            public readonly List<string> Downloaded = new();

            public Fixture()
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Root = new GameObject("WorldTransition_IsolatedFixture");
                Manager = Root.AddComponent<ControlledWorldTransitionManager>();
                Manager.worldParent = Root.transform;
                Manager.unloadUnusedAssetsBeforeLoad = false;
                Manager.resetSplatRenderSystemBeforeLoad = false;
                Manager.loadCleanupFrameDelay = 0;
                Manager.OnWorldLoaded += (id, _) => Loaded.Add(id);
                Manager.OnWorldLoadFailed += (id, _) => Failed.Add(id);
                Manager.OnSplatBytesDownloaded += (id, _) => Downloaded.Add(id);
            }

            public void Dispose()
            {
                if (Root != null) UnityEngine.Object.DestroyImmediate(Root);
            }
        }

        public static async Task RunAsync()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Run transition fixtures in an isolated batch editor.");

            await RunCase(nameof(RapidLoadsOnlyActivateNewest), RapidLoadsOnlyActivateNewest);
            await RunCase(nameof(CancelDuringDownloadIgnoresLateFault), CancelDuringDownloadIgnoresLateFault);
            await RunCase(nameof(CancelDuringProcessingIgnoresLateResult), CancelDuringProcessingIgnoresLateResult);
            await RunCase(nameof(CancelAfterPlacementBeforeRegistrationCleansRoot), CancelAfterPlacementBeforeRegistrationCleansRoot);
            await RunCase(nameof(DisableRejectsLateCompletionAndCanRetry), DisableRejectsLateCompletionAndCanRetry);
            await RunCase(nameof(SceneUnloadRejectsLateCompletion), SceneUnloadRejectsLateCompletion);
            await RunCase(nameof(UnloadAndDefaultRejectPendingCompletion), UnloadAndDefaultRejectPendingCompletion);
            await RunCase(nameof(SameIdRetryKeepsItsLoadingOwnership), SameIdRetryKeepsItsLoadingOwnership);
            await RunCase(nameof(FailedPlacementCleansRootAndCanRetry), FailedPlacementCleansRootAndCanRetry);
            await RunCase(nameof(ReentrantBeforeActivationCannotPublishStaleWorld), ReentrantBeforeActivationCannotPublishStaleWorld);
            await RunCase(nameof(TransformIsAppliedBeforeRendererAndLoadedEvents), TransformIsAppliedBeforeRendererAndLoadedEvents);
            await RunCase(nameof(CancellationCallbackReplacementSurvivesUnload), CancellationCallbackReplacementSurvivesUnload);
            await RunCase(nameof(UnloadCallbackReplacementSurvivesCleanup), UnloadCallbackReplacementSurvivesCleanup);
            await RunCase(nameof(SupersededMulticastStopsBeforeLaterListeners), SupersededMulticastStopsBeforeLaterListeners);
            Debug.Log("[WorldTransitionBatchTests] COMPLETE 14/14 manager transition cases passed.");
        }

        static async Task RunCase(string name, Func<Task> test)
        {
            await test();
            Debug.Log("[WorldTransitionBatchTests] PASS " + name);
        }

        static async Task RapidLoadsOnlyActivateNewest()
        {
            using var f = new Fixture();
            var a = Gate<byte[]>();
            var b = Gate<byte[]>();
            f.Manager.Downloads.Enqueue(a.Task);
            f.Manager.Downloads.Enqueue(b.Task);
            Task<GaussianSplatRenderer> loadA = f.Manager.LoadWorldAsync(World("A"));
            Task<GaussianSplatRenderer> loadB = f.Manager.LoadWorldAsync(World("B"));
            b.SetResult(new byte[] { 2 });
            GaussianSplatRenderer current = await Finish(loadB);
            Assert(current != null, "Newest B must activate.");
            a.SetResult(new byte[] { 1 });
            Assert(await Finish(loadA) == null, "Superseded A must return no renderer.");
            await Task.Yield();
            AssertOnlyLoaded(f, "B");
            Assert(!f.Downloaded.Contains("A"), "Stale bytes must not reach cache consumers.");
            Assert(f.Root.transform.childCount == 1, "Only B content may remain.");
        }

        static async Task CancelDuringDownloadIgnoresLateFault()
        {
            using var f = new Fixture();
            var gate = Gate<byte[]>();
            f.Manager.Downloads.Enqueue(gate.Task);
            var operation = f.Manager.BeginTransition();
            Task<GaussianSplatRenderer> load = f.Manager.LoadWorldAsync(World("cancel-download"), operation);
            f.Manager.CancelTransition(operation);
            Assert(await Finish(load) == null, "Cancellation must release the caller before download completion.");
            Assert(!gate.Task.IsCompleted, "Fixture must still have an in-flight download.");
            Assert(f.Manager.DownloadTokens[0].IsCancellationRequested, "Download must receive cancellation.");
            gate.SetException(new InvalidOperationException("Expected late fixture download failure."));
            await Task.Yield();
            AssertEmpty(f);
            Assert(f.Failed.Count == 0, "Late faults must not publish an error for a canceled request.");
        }

        static async Task CancelDuringProcessingIgnoresLateResult()
        {
            using var f = new Fixture();
            var gate = Gate<RuntimeSplatData>();
            f.Manager.Processing.Enqueue(gate.Task);
            var operation = f.Manager.BeginTransition();
            Task<GaussianSplatRenderer> load = f.Manager.LoadWorldAsync(World("cancel-processing"), operation);
            Assert(f.Manager.ProcessingCalls == 1, "Fixture must enter processing before cancellation.");
            f.Manager.CancelTransition(operation);
            Assert(await Finish(load) == null, "Cancellation must stop waiting for non-cancellable processing.");
            gate.SetResult(new RuntimeSplatData());
            await Task.Yield();
            AssertEmpty(f);
        }

        static async Task CancelAfterPlacementBeforeRegistrationCleansRoot()
        {
            using var f = new Fixture();
            var operation = f.Manager.BeginTransition();
            GameObject placedRoot = null;
            Task cancellation = null;
            f.Manager.RendererDataLoaded = renderer =>
            {
                placedRoot = renderer.gameObject;
                // Queued before the production loader's final yield continuation, so this
                // cancels after placement/activation but before public registration.
                cancellation = CancelOnNextContinuation(f.Manager, operation);
            };
            Assert(await Finish(f.Manager.LoadWorldAsync(World("cancel-placement"), operation)) == null,
                "Canceled placed content must not register.");
            Assert(cancellation != null, "Fixture must reach renderer placement.");
            await cancellation;
            Assert(placedRoot == null, "Canceled renderer root must be destroyed.");
            AssertEmpty(f);
        }

        static async Task CancelOnNextContinuation(WorldLabsWorldManager manager, WorldTransitionOperation operation)
        {
            await Task.Yield();
            manager.CancelTransition(operation);
        }

        static async Task DisableRejectsLateCompletionAndCanRetry()
        {
            using var f = new Fixture();
            var gate = Gate<byte[]>();
            f.Manager.Downloads.Enqueue(gate.Task);
            Task<GaussianSplatRenderer> load = f.Manager.LoadWorldAsync(World("disabled"));
            f.Manager.enabled = false;
            Assert(await Finish(load) == null, "Disabling manager must cancel pending work.");
            gate.SetResult(new byte[] { 1 });
            await Task.Yield();
            AssertEmpty(f);
            f.Manager.enabled = true;
            Assert(await Finish(f.Manager.LoadWorldAsync(World("retry-enabled"))) != null,
                "Manager must accept a fresh request after re-enable.");
            AssertOnlyLoaded(f, "retry-enabled");
        }

        static async Task UnloadAndDefaultRejectPendingCompletion()
        {
            foreach (bool restoreDefault in new[] { false, true })
            {
                using var f = new Fixture();
                var gate = Gate<byte[]>();
                f.Manager.Downloads.Enqueue(gate.Task);
                Task<GaussianSplatRenderer> load = f.Manager.LoadWorldAsync(World("unloaded"));
                if (restoreDefault) f.Manager.RestoreDefaultWorld();
                else f.Manager.UnloadAllWorlds();
                Assert(await Finish(load) == null, "Unload/default must invalidate pending work.");
                gate.SetResult(new byte[] { 1 });
                await Task.Yield();
                AssertEmpty(f);
            }
        }

        static async Task SceneUnloadRejectsLateCompletion()
        {
            using var f = new Fixture();
            var gate = Gate<byte[]>();
            f.Manager.Downloads.Enqueue(gate.Task);
            Task<GaussianSplatRenderer> load = f.Manager.LoadWorldAsync(World("scene-unloaded"));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert(f.Root == null && f.Manager == null, "Fixture scene unload must destroy its manager.");
            Assert(await Finish(load) == null, "Scene unload must release the waiting caller.");
            gate.SetResult(new byte[] { 1 });
            await Task.Yield();
            Assert(f.Loaded.Count == 0 && f.Failed.Count == 0,
                "A destroyed manager must not publish late loaded/failure events.");
            Assert(UnityEngine.Object.FindObjectsByType<GaussianSplatRenderer>(FindObjectsSortMode.None).Length == 0,
                "Scene unload must leave no renderer in the replacement scene.");
        }

        static async Task SameIdRetryKeepsItsLoadingOwnership()
        {
            using var f = new Fixture();
            var oldGate = Gate<byte[]>();
            var retryGate = Gate<byte[]>();
            f.Manager.Downloads.Enqueue(oldGate.Task);
            f.Manager.Downloads.Enqueue(retryGate.Task);
            Task<GaussianSplatRenderer> old = f.Manager.LoadWorldAsync(World("same"));
            Task<GaussianSplatRenderer> retry = f.Manager.LoadWorldAsync(World("same"));
            Assert(await Finish(old) == null, "Old same-ID request must cancel promptly.");
            Assert(f.Manager.IsWorldLoading("same"), "Old finally must not remove newer same-ID loading ownership.");
            oldGate.SetResult(new byte[] { 1 });
            await Task.Yield();
            Assert(f.Manager.IsWorldLoading("same"), "Late old completion must retain retry loading state.");
            retryGate.SetResult(new byte[] { 2 });
            Assert(await Finish(retry) != null, "Same-ID retry must succeed.");
            AssertOnlyLoaded(f, "same");
        }

        static async Task FailedPlacementCleansRootAndCanRetry()
        {
            using var f = new Fixture();
            GameObject root = null;
            GaussianSplatRenderer failed = await Finish(f.Manager.LoadWorldAsync(World("placement-fails"),
                beforeRendererCreated: value =>
                {
                    root = value;
                    throw new InvalidOperationException("Expected fixture placement failure.");
                }));
            Assert(failed == null && root == null, "Throwing placement callback must destroy its private root.");
            AssertEmpty(f);
            Assert(f.Failed.Count == 1, "Current failure must publish exactly one failure event.");
            Assert(await Finish(f.Manager.LoadWorldAsync(World("placement-fails"))) != null,
                "Failed world must be retryable.");
            AssertOnlyLoaded(f, "placement-fails");
        }

        static async Task ReentrantBeforeActivationCannotPublishStaleWorld()
        {
            using var f = new Fixture();
            var gate = Gate<byte[]>();
            Task<GaussianSplatRenderer> replacement = null;
            GameObject staleRoot = null;
            f.Manager.OnWorldBeforeActivated += (id, renderer) =>
            {
                if (id != "A") return;
                staleRoot = renderer.gameObject;
                f.Manager.Downloads.Enqueue(gate.Task);
                replacement = f.Manager.LoadWorldAsync(World("B"));
            };
            Assert(await Finish(f.Manager.LoadWorldAsync(World("A"))) == null,
                "Reentrant request must prevent stale A activation.");
            Assert(replacement != null && staleRoot == null, "Replacement must start and stale root must be destroyed.");
            Assert(f.Loaded.Count == 0 && f.Manager.IsWorldLoading("B"), "A must publish no loaded event.");
            gate.SetResult(new byte[] { 2 });
            Assert(await Finish(replacement) != null, "Reentrant B must complete normally.");
            AssertOnlyLoaded(f, "B");
        }

        static async Task TransformIsAppliedBeforeRendererAndLoadedEvents()
        {
            using var f = new Fixture();
            Vector3 savedPosition = new(3, 4, 5);
            bool beforeRenderer = false;
            bool beforeActivation = false;
            f.Manager.OnWorldBeforeActivated += (_, renderer) =>
            {
                Assert(beforeRenderer && renderer.transform.position == savedPosition,
                    "Saved transform must be visible before activation consumers run.");
                Assert(!renderer.gameObject.activeSelf, "Before-activation consumers must see a private root.");
                beforeActivation = true;
            };
            f.Manager.OnWorldLoaded += (_, renderer) =>
                Assert(beforeActivation && renderer.transform.position == savedPosition,
                    "Loaded/spawn consumers must see the restored transform.");
            GaussianSplatRenderer loaded = await Finish(f.Manager.LoadWorldAsync(World("ordered"),
                beforeRendererCreated: root =>
                {
                    Assert(root.GetComponent<GaussianSplatRenderer>() == null,
                        "Saved transform hook must run before renderer creation.");
                    root.transform.position = savedPosition;
                    beforeRenderer = true;
                }));
            Assert(loaded != null && beforeActivation, "Ordered load must complete.");
            AssertOnlyLoaded(f, "ordered");
        }

        static async Task CancellationCallbackReplacementSurvivesUnload()
        {
            foreach (bool restoreDefault in new[] { false, true })
            {
                using var f = new Fixture();
                Assert(await Finish(f.Manager.LoadWorldAsync(World("A"))) != null, "Initial A must load.");
                f.Loaded.Clear();
                WorldTransitionOperation previous = f.Manager.CurrentTransition;
                Task<GaussianSplatRenderer> replacement = null;
                bool replacementStarted = false;
                int previousCancellationEvents = 0;
                f.Manager.OnTransitionCancelled += operation =>
                {
                    if (operation == previous) previousCancellationEvents++;
                    if (operation == previous && !replacementStarted)
                    {
                        replacementStarted = true;
                        replacement = f.Manager.LoadWorldAsync(World("B"));
                    }
                };
                // The default asset must never be touched once a callback has started B.
                var defaultAsset = ScriptableObject.CreateInstance<GaussianSplatAsset>();
                defaultAsset.name = "UnusedDefaultTransitionFixture";
                f.Manager.defaultAsset = defaultAsset;
                try
                {
                    if (restoreDefault) f.Manager.RestoreDefaultWorld();
                    else f.Manager.UnloadAllWorlds();
                    Assert(replacement != null, "Cancellation listener must start B.");
                    Assert(await Finish(replacement) != null, "Unload/default cleanup destroyed callback-started B.");
                    Assert(previousCancellationEvents == 1, "A cancellation must be published exactly once during reentry.");
                    AssertOnlyLoaded(f, "B");
                    Assert(f.Root.transform.childCount == 1, "Old cleanup left a default or duplicate root alongside B.");
                }
                finally
                {
                    f.Manager.defaultAsset = null;
                    UnityEngine.Object.DestroyImmediate(defaultAsset);
                }
            }
        }

        static async Task UnloadCallbackReplacementSurvivesCleanup()
        {
            foreach (bool prepare in new[] { false, true })
            {
                using var f = new Fixture();
                Assert(await Finish(f.Manager.LoadWorldAsync(World("A"))) != null, "Initial A must load.");
                f.Loaded.Clear();
                Task<GaussianSplatRenderer> replacement = null;
                f.Manager.OnWorldUnloaded += id =>
                {
                    if (id == "A" && replacement == null)
                        replacement = f.Manager.LoadWorldAsync(World("B"));
                };
                if (prepare)
                {
                    bool canceled = false;
                    try { await f.Manager.PrepareForWorldLoadAsync(f.Manager.CurrentTransition); }
                    catch (OperationCanceledException) { canceled = true; }
                    Assert(canceled, "Cleanup must stop after its unload listener supersedes its operation.");
                }
                else
                {
                    WorldTransitionOperation stale = f.Manager.BeginTransition();
                    Assert(!stale.IsCurrent, "An outer Begin must not overwrite the callback's newer operation.");
                }
                Assert(replacement != null, "Unloaded listener must start B.");
                Assert(await Finish(replacement) != null, "Outer cleanup removed callback-started B.");
                AssertOnlyLoaded(f, "B");
            }
        }

        static async Task SupersededMulticastStopsBeforeLaterListeners()
        {
            foreach (bool duringProgress in new[] { false, true })
            {
                using var f = new Fixture();
                Task<GaussianSplatRenderer> replacement = null;
                bool staleListenerCalled = false;
                if (duringProgress)
                {
                    f.Manager.OnWorldLoadProgress += (id, _) =>
                    {
                        if (id == "A") replacement = f.Manager.LoadWorldAsync(World("B"));
                    };
                    f.Manager.OnWorldLoadProgress += (id, _) => staleListenerCalled |= id == "A";
                }
                else
                {
                    f.Manager.OnWorldLoaded += (id, _) =>
                    {
                        if (id == "A") replacement = f.Manager.LoadWorldAsync(World("B"));
                    };
                    f.Manager.OnWorldLoaded += (id, _) => staleListenerCalled |= id == "A";
                }
                Assert(await Finish(f.Manager.LoadWorldAsync(World("A"))) == null,
                    "A must return no renderer after its callback starts B.");
                Assert(replacement != null && await Finish(replacement) != null, "Callback-started B must finish.");
                Assert(!staleListenerCalled, "Later multicast listeners received a superseded A event.");
                Assert(f.Manager.LoadedWorldIds.Count == 1 && f.Manager.IsWorldLoaded("B") &&
                       f.Manager.LastLoadedWorld?.world_id == "B", "Only callback-started B may remain active.");
            }
        }

        static World World(string id) => new()
        {
            world_id = id,
            display_name = id,
            assets = new WorldAssets
            {
                splats = new SplatAssets { spz_urls = new Dictionary<string, string> { ["500k"] = "fixture://" + id } }
            }
        };

        static TaskCompletionSource<T> Gate<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        static async Task<T> Finish<T>(Task<T> task)
        {
            if (await Task.WhenAny(task, Task.Delay(10000)) != task)
                throw new TimeoutException("Transition did not settle within ten seconds.");
            return await task;
        }

        static void AssertOnlyLoaded(Fixture f, string id)
        {
            Assert(f.Loaded.Count == 1 && f.Loaded[0] == id, "Only " + id + " may publish a loaded event.");
            Assert(f.Manager.LoadedWorldIds.Count == 1 && f.Manager.IsWorldLoaded(id), "Only current world may be registered.");
            Assert(f.Manager.LastLoadedWorld?.world_id == id, "LastLoadedWorld must match activated content.");
            Assert(!f.Manager.IsWorldLoading(id), "Completed world must leave loading state.");
        }

        static void AssertEmpty(Fixture f)
        {
            Assert(f.Manager.LoadedWorldIds.Count == 0 && f.Manager.LastLoadedWorld == null,
                "Canceled/failed transitions must leave no active world metadata.");
            Assert(f.Loaded.Count == 0 && f.Root.transform.childCount == 0,
                "Canceled/failed transitions must publish no world and leave no owned content.");
        }

        static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
