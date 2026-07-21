using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace PickleGit.Services.Git
{
    /// <summary>
    /// Dedicated background thread that serializes all git operations (LibGit2Sharp
    /// and git.exe) for one repository. LibGit2Sharp's Repository is not thread-safe;
    /// funneling every call through this queue removes UI-thread stalls and prevents
    /// concurrent access races between refresh, diff loading, and staging operations.
    ///
    /// Work items must never block on the WPF Dispatcher (Dispatcher.Invoke from a
    /// work item is only safe because the UI thread awaits — never synchronously
    /// waits on — tasks returned from RunAsync).
    /// </summary>
    public sealed class GitExecutor : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();
        private readonly Thread _thread;
        private volatile int _threadId;

        public GitExecutor()
        {
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "PickleGit-Git"
            };
            _thread.Start();
        }

        private void Loop()
        {
            _threadId = Thread.CurrentThread.ManagedThreadId;
            foreach (var work in _queue.GetConsumingEnumerable())
            {
                work();
            }
        }

        public Task RunAsync(Action op)
        {
            return RunAsync(() => { op(); return true; });
        }

        public Task<T> RunAsync<T>(Func<T> op)
        {
            // Nested calls from a work item execute inline — queueing them would
            // deadlock (the queue is drained by the very thread that is waiting).
            if (Thread.CurrentThread.ManagedThreadId == _threadId)
            {
                try { return Task.FromResult(op()); }
                catch (Exception ex) { return Task.FromException<T>(ex); }
            }

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _queue.Add(() =>
                {
                    try { tcs.SetResult(op()); }
                    catch (Exception ex) { tcs.SetException(ex); }
                });
            }
            catch (InvalidOperationException)
            {
                // Disposed — adding completed
                tcs.SetCanceled();
            }
            return tcs.Task;
        }

        public void Dispose()
        {
            try { _queue.CompleteAdding(); }
            catch { }
        }
    }
}
