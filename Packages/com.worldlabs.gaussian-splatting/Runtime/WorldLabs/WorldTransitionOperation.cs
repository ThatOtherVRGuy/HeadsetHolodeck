using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace WorldLabs.Runtime
{
    /// <summary>
    /// A single world request and the content it owns. All state/Unity object methods must
    /// be called on Unity's thread. Cancellation stops local work, not a remote provider job.
    /// Completed operations retain ownership until the next transition or explicit unload.
    /// </summary>
    public sealed class WorldTransitionOperation
    {
        readonly CancellationTokenSource cancellation = new();
        readonly List<UnityEngine.Object> content = new();
        readonly Func<bool> ownerIsCurrent;
        bool cancelled;

        internal WorldTransitionOperation(Func<bool> ownerIsCurrent = null)
        {
            this.ownerIsCurrent = ownerIsCurrent;
            Token = cancellation.Token;
        }

        public static WorldTransitionOperation CreateStandalone() => new();
        public CancellationToken Token { get; }
        public bool IsCurrent => !cancelled && (ownerIsCurrent == null || ownerIsCurrent());
        public bool IsCompleted { get; internal set; }

        public void ThrowIfStale()
        {
            if (!IsCurrent) throw new OperationCanceledException(Token);
        }

        public void Track(UnityEngine.Object value)
        {
            if (value == null) return;
            if (!IsCurrent)
            {
                DestroyOwned(value);
                ThrowIfStale();
            }
            if (!content.Contains(value)) content.Add(value);
        }

        public void Cancel()
        {
            if (cancelled) return;
            cancelled = true;
            try { cancellation.Cancel(); }
            catch (Exception ex) { Debug.LogException(ex); }
            finally
            {
                foreach (var value in content)
                {
                    try { DestroyOwned(value); }
                    catch (Exception ex) { Debug.LogException(ex); }
                }
                content.Clear();
                cancellation.Dispose();
            }
        }

        public static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (value is GameObject go) go.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        /// <summary>Stop waiting promptly, while observing a non-cancellable task's late fault.</summary>
        public async Task<T> AwaitAsync<T>(Task<T> task)
        {
            if (!IsCurrent)
            {
                _ = ObserveAsync(task);
                ThrowIfStale();
            }
            var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (Token.Register(() => stopped.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, stopped.Task) != task)
                {
                    _ = ObserveAsync(task);
                    ThrowIfStale();
                }
            }
            T result = await task;
            ThrowIfStale();
            return result;
        }

        static async Task ObserveAsync(Task task)
        {
            try { await task; }
            catch (Exception) { /* The owner already cancelled; never publish a stale error. */ }
        }
    }
}
