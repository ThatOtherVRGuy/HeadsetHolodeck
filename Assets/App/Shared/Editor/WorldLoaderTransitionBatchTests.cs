using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GaussianSplatting.Runtime;
using SpeechIntent;
using Unity.Collections;
using UnityEngine;
using WorldLabs.Runtime;
using WorldLabs.Runtime.Tools;

namespace HeadsetHolodeck.EditorTests
{
    /// <summary>Isolated asynchronous fixtures. Invoke only from a dedicated batch test adapter.</summary>
    public static class WorldLoaderTransitionBatchTests
    {
        public static async Task RunAllAsync()
        {
            await TestDownloadCancellationAndLateFault();
            await TestSupersededPlacementAndRetry();
            await TestPlacementCancellation();
            await TestCallerOwnedFailureCanRetry();
            await TestReentrantCancellationKeepsNewestRequest();
            await TestLoaderDisable();
            await TestFloorDisable();
            await TestThrowingTransformCallback();
            await TestEarlyCancellationDisposesSplats();
            Debug.Log("[WorldLoaderTransitionBatchTests] 9/9 transition fixtures passed.");
        }

        sealed class Fixture : IDisposable
        {
            public readonly GameObject root = new("WorldLoaderTransition_Fixture");
            public readonly WorldLabsWorldManager manager;
            public readonly RuntimeSplatFloorLoader floor;
            public readonly GatedLocalSplatLoader loader;

            public Fixture()
            {
                manager = root.AddComponent<ControlledWorldTransitionManager>();
                manager.unloadUnusedAssetsBeforeLoad = false;
                manager.resetSplatRenderSystemBeforeLoad = false;
                manager.loadCleanupFrameDelay = 0;
                floor = root.AddComponent<ControlledTransitionFloorLoader>();
                floor.quality = RuntimeSplatFloorLoader.SplatQualityPreset.VeryHigh;
                loader = root.AddComponent<GatedLocalSplatLoader>();
                loader.worldManager = manager;
                loader.floorLoader = floor;
                loader.localBasePath = "/isolated-fixture-no-files";
            }

            public Task<bool> Load(string name, WorldTransitionOperation operation = null) =>
                loader.LoadAndWaitAsync(name + ".spz", name,
                    RuntimeSplatFloorLoader.SplatSourceKind.LooseSplat, null, operation: operation);

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(root);
                foreach (GameObject result in loader.createdRoots)
                    if (result != null) UnityEngine.Object.DestroyImmediate(result);
            }
        }

        static async Task TestDownloadCancellationAndLateFault()
        {
            using var fixture = new Fixture();
            Task<bool> request = fixture.Load("DownloadA");
            await Until(() => fixture.loader.reads.Count == 1, "download was not started");
            TaskCompletionSource<byte[]> read = fixture.loader.reads.Dequeue();
            fixture.loader.CancelLoad();
            Assert(!await Within(request), "CancelLoad must settle a pending download as false.");
            read.SetException(new InvalidOperationException("intentional late read failure"));
            await Pump();
            Assert(fixture.loader.placements.Count == 0, "Canceled download reached placement.");
            Assert(fixture.manager.LoadedWorldIds.Count == 0, "Canceled download registered content.");
        }

        static async Task TestSupersededPlacementAndRetry()
        {
            using var fixture = new Fixture();
            Task<bool> requestA = fixture.Load("A");
            GatedLocalSplatLoader.Placement gateA = await ReachPlacement(fixture);
            WorldTransitionOperation operationA = fixture.manager.CurrentTransition;
            Task<bool> requestB = fixture.Load("B");
            GatedLocalSplatLoader.Placement gateB = await ReachPlacement(fixture);
            Assert(!await Within(requestA), "Superseded A must settle without waiting for its placement.");
            RuntimeSplatFloorLoader.LoadResult resultB = fixture.loader.Result("B");
            gateB.completion.SetResult(resultB);
            Assert(await Within(requestB), "B did not succeed after superseding A.");
            RuntimeSplatFloorLoader.LoadResult resultA = fixture.loader.Result("LateA");
            gateA.completion.SetResult(resultA);
            await Until(() => resultA.gameObject == null, "Late A root was not reaped.");
            Assert(resultA.gameObject == null, "Late A root survived supersession.");
            Assert(resultB.gameObject != null && resultB.gameObject.activeSelf, "Late A removed B.");
            Assert(fixture.manager.IsWorldLoaded(gateB.worldId), "B lost manager registration.");
            Assert(!operationA.IsCurrent, "Superseded operation remains current.");
            Assert(fixture.manager.LoadedWorldIds.Count == 1, "More than one world was retained.");
        }

        static async Task TestPlacementCancellation()
        {
            using var fixture = new Fixture();
            WorldTransitionOperation operation = fixture.manager.BeginTransition();
            Task<bool> request = fixture.Load("PlacementCancel", operation);
            GatedLocalSplatLoader.Placement gate = await ReachPlacement(fixture);
            fixture.loader.CancelLoad();
            Assert(!await Within(request), "Cancellation did not settle pending placement.");
            RuntimeSplatFloorLoader.LoadResult late = fixture.loader.Result("LateCanceledPlacement");
            gate.completion.SetResult(late);
            await Until(() => late.gameObject == null, "Late canceled root was not reaped.");
            Assert(late.gameObject == null && !operation.IsCurrent, "Canceled placement left content/current identity.");

            WorldTransitionOperation retryOperation = fixture.manager.BeginTransition();
            Task<bool> retry = fixture.Load("Retry", retryOperation);
            GatedLocalSplatLoader.Placement retryGate = await ReachPlacement(fixture);
            retryGate.completion.SetResult(fixture.loader.Result("Retry"));
            Assert(await Within(retry), "A retry after canceled placement failed.");
            Assert(!retryOperation.IsCompleted, "Loader completed an operation owned by its restore caller.");
        }

        static async Task TestCallerOwnedFailureCanRetry()
        {
            using var fixture = new Fixture();
            WorldTransitionOperation operation = fixture.manager.BeginTransition();
            Task<bool> failed = fixture.Load("FailedCache", operation);
            await Until(() => fixture.loader.reads.Count > 0, "failed cache read was not requested");
            fixture.loader.reads.Dequeue().SetException(new InvalidOperationException("intentional cache failure"));
            Assert(!await Within(failed), "Failed cache unexpectedly succeeded.");
            Assert(operation.IsCurrent, "A failed cache attempt canceled its caller's fallback operation.");
            Task<bool> fallback = fixture.Load("Fallback", operation);
            GatedLocalSplatLoader.Placement gate = await ReachPlacement(fixture);
            gate.completion.SetResult(fixture.loader.Result("Fallback"));
            Assert(await Within(fallback), "Fallback source could not reuse its caller's current operation.");
            Assert(operation.IsCurrent && !operation.IsCompleted, "Loader changed caller-owned operation lifetime.");
        }

        static async Task TestReentrantCancellationKeepsNewestRequest()
        {
            using var fixture = new Fixture();
            Task<bool> first = fixture.Load("First");
            await Until(() => fixture.loader.reads.Count > 0, "first read was not requested");
            TaskCompletionSource<byte[]> firstRead = fixture.loader.reads.Dequeue();
            WorldTransitionOperation firstOperation = fixture.manager.CurrentTransition;
            bool started = false;
            Task<bool> newest = null;
            fixture.manager.OnTransitionCancelled += operation =>
            {
                if (operation != firstOperation || started) return;
                started = true;
                newest = fixture.Load("Newest");
            };
            Task<bool> outer = fixture.Load("SupersededOuter");
            Assert(!await Within(outer) && !await Within(first), "Outer/previous requests must yield to callback-started request.");
            Assert(newest != null, "Cancellation listener did not start the newer request.");
            GatedLocalSplatLoader.Placement gate = await ReachPlacement(fixture);
            gate.completion.SetResult(fixture.loader.Result("Newest"));
            Assert(await Within(newest), "Outer loader call overwrote its callback's newer request.");
            firstRead.SetResult(new byte[] { 1 });
            await Pump();
            Assert(fixture.manager.IsWorldLoaded(gate.worldId) && fixture.manager.LoadedWorldIds.Count == 1,
                "Only callback-started content may remain registered.");
        }

        static async Task TestLoaderDisable()
        {
            using var fixture = new Fixture();
            Task<bool> request = fixture.Load("Disable");
            GatedLocalSplatLoader.Placement gate = await ReachPlacement(fixture);
            fixture.loader.enabled = false;
            Assert(!await Within(request), "Disabling loader did not settle its awaiter.");
            RuntimeSplatFloorLoader.LoadResult late = fixture.loader.Result("LateDisabled");
            gate.completion.SetResult(late);
            await Until(() => late.gameObject == null, "Late canceled root was not reaped.");
            Assert(late.gameObject == null, "Disabled loader retained a late root.");
            Assert(fixture.manager.LoadedWorldIds.Count == 0, "Disabled loader registered a late world.");
        }

        static async Task TestFloorDisable()
        {
            using var fixture = new Fixture();
            NativeArray<InputSplatData> splats = TinyPly();
            bool callbackCalled = false;
            Task<RuntimeSplatFloorLoader.LoadResult> placement = fixture.floor.LoadPlacedRuntimeWorldFromSplatsAsync(
                splats, worldName: "FloorDisable", beforeRendererCreated: _ => callbackCalled = true);
            fixture.floor.enabled = false;
            await ExpectCancellation(placement);
            await Until(() => IsDisposed(splats), "Late floor processing did not release native splats.");
            Assert(!callbackCalled, "Disabled floor loader reached renderer creation.");
            Assert(fixture.root.transform.childCount == 0, "Disabled floor loader left an orphan root.");
        }

        static async Task TestThrowingTransformCallback()
        {
            using var fixture = new Fixture();
            GameObject created = null;
            bool sawNoRenderer = false;
            bool sawInactiveRoot = false;
            NativeArray<InputSplatData> splats = TinyPly();
            try
            {
                await Within(fixture.floor.LoadPlacedRuntimeWorldFromSplatsAsync(splats,
                    worldName: "ThrowingTransform", beforeRendererCreated: root =>
                    {
                        created = root;
                        sawNoRenderer = root.GetComponent<GaussianSplatRenderer>() == null;
                        sawInactiveRoot = !root.activeSelf;
                        root.transform.localPosition = new Vector3(3f, 4f, 5f);
                        root.transform.localRotation = Quaternion.Euler(0f, 37f, 0f);
                        throw new TransformFixtureException();
                    }));
                throw new InvalidOperationException("The transform callback's exception was swallowed.");
            }
            catch (TransformFixtureException) { }
            Assert(sawNoRenderer && sawInactiveRoot, "Saved transforms must run before renderer creation/activation.");
            Assert(created == null && fixture.root.transform.childCount == 0, "Throwing transform callback orphaned its root.");
            Assert(IsDisposed(splats), "Failed placement did not release its input splats.");
            Assert(fixture.floor.floorAnalysis.positionTransform == default,
                "One load mutated the component's shared floor orientation options.");
        }

        static async Task TestEarlyCancellationDisposesSplats()
        {
            using var fixture = new Fixture();
            NativeArray<InputSplatData> splats = TinyPly();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await ExpectCancellation(fixture.floor.LoadPlacedRuntimeWorldFromSplatsAsync(splats,
                cancellationToken: cancellation.Token));
            Assert(IsDisposed(splats), "Pre-canceled floor call leaked the transferred NativeArray.");
            Assert(fixture.root.transform.childCount == 0, "Pre-canceled floor call created content.");
        }

        static NativeArray<InputSplatData> TinyPly()
        {
            MethodInfo builder = typeof(RuntimePlyReaderBatchTests).GetMethod(
                "CreateMinimalBinaryGaussianPly", BindingFlags.NonPublic | BindingFlags.Static);
            Assert(builder != null, "Existing isolated PLY fixture was not found.");
            RuntimePlyReader.ReadFromBytes((byte[])builder.Invoke(null, null), out NativeArray<InputSplatData> splats);
            return splats;
        }

        static bool IsDisposed(NativeArray<InputSplatData> splats)
        {
            try { _ = splats[0]; return false; }
            catch (ObjectDisposedException) { return true; }
            catch (InvalidOperationException) { return true; }
        }

        static async Task<GatedLocalSplatLoader.Placement> ReachPlacement(Fixture fixture)
        {
            await Until(() => fixture.loader.reads.Count > 0, "loader did not request fixture bytes");
            fixture.loader.reads.Dequeue().SetResult(new byte[] { 1 });
            await Until(() => fixture.loader.placements.Count > 0, "loader did not reach fixture placement");
            return fixture.loader.placements.Dequeue();
        }

        static async Task ExpectCancellation<T>(Task<T> task)
        {
            try { await Within(task); }
            catch (OperationCanceledException) { return; }
            throw new InvalidOperationException("Expected canceled floor placement.");
        }

        static async Task<T> Within<T>(Task<T> task)
        {
            Assert(await Task.WhenAny(task, Task.Delay(15000)) == task, "Fixture timed out.");
            return await task;
        }

        static async Task Until(Func<bool> condition, string message)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (!condition())
            {
                Assert(DateTime.UtcNow < deadline, message);
                await Task.Yield();
            }
        }

        static async Task Pump()
        {
            // Let cancellation and late-result cleanup traverse the captured Unity context.
            for (int i = 0; i < 12; ++i) await Task.Yield();
        }

        static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        sealed class TransformFixtureException : Exception { }
    }

    [ExecuteAlways]
    public sealed class ControlledTransitionFloorLoader : RuntimeSplatFloorLoader { }

    [ExecuteAlways]
    public sealed class GatedLocalSplatLoader : LocalRemoteSplatLoader
    {
        public sealed class Placement
        {
            public string worldId;
            public readonly TaskCompletionSource<RuntimeSplatFloorLoader.LoadResult> completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public readonly Queue<TaskCompletionSource<byte[]>> reads = new();
        public readonly Queue<Placement> placements = new();
        public readonly List<GameObject> createdRoots = new();

        protected override Task<byte[]> ReadBytesAsync(string path, bool isUrl, CancellationToken token)
        {
            var read = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            reads.Enqueue(read);
            return read.Task; // Intentionally ignores cancellation to reproduce late provider/I/O behavior.
        }

        protected override Task<RuntimeSplatFloorLoader.LoadResult> PlaceWorldAsync(byte[] bytes,
            string extension, string worldId, string worldName, RuntimeSplatFloorLoader.SplatSourceKind sourceKind,
            SplatSpawnMetadata metadata, Action<GameObject> beforeRendererCreated,
            WorldTransitionOperation operation, CancellationToken token)
        {
            var placement = new Placement { worldId = worldId };
            placements.Enqueue(placement);
            return placement.completion.Task;
        }

        public RuntimeSplatFloorLoader.LoadResult Result(string name)
        {
            var root = new GameObject("WorldLoaderTransition_" + name);
            root.SetActive(false);
            GaussianSplatRenderer renderer = root.AddComponent<GaussianSplatRenderer>();
            renderer.enabled = false; // No GPU initialization or real source bytes in gated fixtures.
            root.SetActive(true);
            createdRoots.Add(root);
            return new RuntimeSplatFloorLoader.LoadResult { gameObject = root, renderer = renderer };
        }
    }
}
