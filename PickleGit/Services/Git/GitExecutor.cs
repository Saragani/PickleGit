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
        private bool _disposed;

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
            catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException)
            {
                // InvalidOperationException: CompleteAdding() already called. ObjectDisposedException:
                // Dispose() has since fully released the queue too. Either way this executor is
                // shutting down and will never run the work.
                tcs.SetCanceled();
            }
            return tcs.Task;
        }

        /// <summary>Idempotent, and blocks (briefly) until the background thread has actually
        /// drained and exited — not just been asked to stop. <see cref="GitService.Dispose"/> calls
        /// this immediately before disposing the LibGit2Sharp Repository the executor thread reads;
        /// without waiting for the thread to actually finish, a work item still executing at the
        /// moment of Dispose() could race a concurrent Repository.Dispose() on the calling thread —
        /// undefined behavior, since LibGit2Sharp's Repository is not thread-safe. Confirmed as a
        /// real gap via code review: closing a repo tab calls this synchronously on the UI thread
        /// (RepositoryViewModel.Dispose -> GitService.Dispose), and while CloseTab already skips
        /// busy tabs, that guard doesn't cover every executor call in this codebase (e.g. the
        /// Reopen() the external-change watcher issues before IsBusy is set — see
        /// RepositoryViewModel.OnRepoChangedExternally) — this makes the wait unconditional instead
        /// of relying on every caller getting that timing right.
        ///
        /// The Join is timed, not indefinite, for two reasons: a work item disposing its own
        /// GitService from inside itself would otherwise deadlock joining its own thread — not a
        /// real call path today (Dispose only ever runs from the UI thread on tab close), but this
        /// skips the join entirely rather than hang if it ever is; and a wedged git.exe call already
        /// has its own cancellation path elsewhere, so this is a last-resort bound, not an expected
        /// wait. The thread is IsBackground=true regardless, so the process can still exit cleanly
        /// even on the rare path where the timeout is actually hit.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _queue.CompleteAdding(); } catch { }
            if (Thread.CurrentThread.ManagedThreadId != _threadId)
                _thread.Join(TimeSpan.FromSeconds(5));
            _queue.Dispose();
        }
    }
}
