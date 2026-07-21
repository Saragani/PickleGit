using System;
using System.IO;
using System.Threading;

namespace PickleGit.Services
{
    public enum RepoChangeKind
    {
        /// <summary>Only working-directory / index content changed — refresh status lists.</summary>
        WorkingDir,
        /// <summary>Refs / HEAD changed — a full refresh is needed.</summary>
        Refs
    }

    /// <summary>
    /// Watches a repository for external changes and raises a single debounced
    /// <see cref="Changed"/> event (400 ms coalescing window).
    ///
    /// Two watchers: the working directory (ignoring everything under .git) maps to
    /// <see cref="RepoChangeKind.WorkingDir"/>; the .git directory itself maps
    /// index changes to WorkingDir and HEAD/refs/packed-refs changes to
    /// <see cref="RepoChangeKind.Refs"/> (object-database churn is ignored).
    ///
    /// The app's own git operations are wrapped in <see cref="Suppress"/> so they
    /// don't trigger redundant refreshes; events that arrive while suppressed are
    /// coalesced into one event raised after the operation completes.
    ///
    /// <see cref="Changed"/> is raised on a threadpool thread — subscribers must
    /// marshal to the dispatcher themselves.
    /// </summary>
    public sealed class RepositoryWatcher : IDisposable
    {
        private const int DebounceMs = 400;
        private const int StormEventLimit = 5000;      // events per window before backing off
        private const int StormWindowMs = 10_000;
        private const int StormBackoffMs = 60_000;

        private readonly string _workDir;
        private readonly string _gitDir;
        private FileSystemWatcher _workDirWatcher;
        private FileSystemWatcher _gitDirWatcher;
        private readonly Timer _debounceTimer;
        private Timer _stormRecoveryTimer;

        private readonly object _lock = new object();
        private RepoChangeKind _pendingKind = RepoChangeKind.WorkingDir;
        private bool _hasPending;
        private int _suppressCount;
        private bool _pendingWhileSuppressed;

        private int _stormEventCount;
        private long _stormWindowStartTicks;
        private bool _disposed;

        public event Action<RepoChangeKind> Changed;

        public RepositoryWatcher(string workingDirectory, string gitDirectory)
        {
            _workDir = TrimSeparator(workingDirectory);
            _gitDir = TrimSeparator(gitDirectory);
            _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);

            try { _workDirWatcher = CreateWorkDirWatcher(); }
            catch { _workDirWatcher = null; }
            try { _gitDirWatcher = CreateGitDirWatcher(); }
            catch { _gitDirWatcher = null; }
        }

        private static string TrimSeparator(string path)
            => string.IsNullOrEmpty(path) ? path : path.TrimEnd('\\', '/');

        private FileSystemWatcher CreateWorkDirWatcher()
        {
            if (string.IsNullOrEmpty(_workDir) || !Directory.Exists(_workDir)) return null;
            var w = new FileSystemWatcher(_workDir)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = 64 * 1024,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName |
                               NotifyFilters.DirectoryName | NotifyFilters.Size
            };
            w.Changed += OnWorkDirEvent;
            w.Created += OnWorkDirEvent;
            w.Deleted += OnWorkDirEvent;
            w.Renamed += OnWorkDirRenamed;
            w.Error += OnWatcherError;
            w.EnableRaisingEvents = true;
            return w;
        }

        private FileSystemWatcher CreateGitDirWatcher()
        {
            if (string.IsNullOrEmpty(_gitDir) || !Directory.Exists(_gitDir)) return null;
            var w = new FileSystemWatcher(_gitDir)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = 64 * 1024,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName
            };
            w.Changed += OnGitDirEvent;
            w.Created += OnGitDirEvent;
            w.Deleted += OnGitDirEvent;
            w.Renamed += OnGitDirRenamed;
            w.Error += OnWatcherError;
            w.EnableRaisingEvents = true;
            return w;
        }

        // ── Event classification ──────────────────────────────────────────────

        private void OnWorkDirEvent(object sender, FileSystemEventArgs e) => HandleWorkDirPath(e.FullPath);
        private void OnWorkDirRenamed(object sender, RenamedEventArgs e)
        {
            HandleWorkDirPath(e.OldFullPath);
            HandleWorkDirPath(e.FullPath);
        }

        private void HandleWorkDirPath(string fullPath)
        {
            if (IsUnderGitDir(fullPath)) return;
            if (RegisterStormEvent()) return;
            Signal(RepoChangeKind.WorkingDir);
        }

        private void OnGitDirEvent(object sender, FileSystemEventArgs e) => HandleGitDirPath(e.FullPath);
        private void OnGitDirRenamed(object sender, RenamedEventArgs e)
        {
            HandleGitDirPath(e.OldFullPath);
            HandleGitDirPath(e.FullPath);
        }

        private void HandleGitDirPath(string fullPath)
        {
            var rel = GetRelative(_gitDir, fullPath);
            if (rel == null) return;

            // git.exe writes "<file>.lock" then renames; classify by the real name
            if (rel.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
                rel = rel.Substring(0, rel.Length - 5);

            if (rel.Equals("index", StringComparison.OrdinalIgnoreCase))
            {
                Signal(RepoChangeKind.WorkingDir);
                return;
            }

            if (rel.Equals("HEAD", StringComparison.OrdinalIgnoreCase) ||
                rel.Equals("packed-refs", StringComparison.OrdinalIgnoreCase) ||
                rel.Equals("MERGE_HEAD", StringComparison.OrdinalIgnoreCase) ||
                rel.Equals("CHERRY_PICK_HEAD", StringComparison.OrdinalIgnoreCase) ||
                rel.Equals("REVERT_HEAD", StringComparison.OrdinalIgnoreCase) ||
                rel.Equals("FETCH_HEAD", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("refs" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("refs/", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("rebase-merge", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("rebase-apply", StringComparison.OrdinalIgnoreCase))
            {
                Signal(RepoChangeKind.Refs);
            }
            // everything else (objects/, logs/, config churn) is ignored
        }

        private bool IsUnderGitDir(string fullPath)
        {
            return !string.IsNullOrEmpty(_gitDir) &&
                   fullPath != null &&
                   fullPath.StartsWith(_gitDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRelative(string root, string fullPath)
        {
            if (string.IsNullOrEmpty(root) || fullPath == null) return null;
            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return null;
            return fullPath.Substring(root.Length + 1);
        }

        // ── Storm protection (e.g. npm install in the workdir) ───────────────

        /// <returns>true when the event should be dropped because we're backing off.</returns>
        private bool RegisterStormEvent()
        {
            var now = MonotonicClock.NowMs();
            lock (_lock)
            {
                if (now - _stormWindowStartTicks > StormWindowMs)
                {
                    _stormWindowStartTicks = now;
                    _stormEventCount = 0;
                }
                if (++_stormEventCount < StormEventLimit) return false;
                if (_stormEventCount > StormEventLimit) return true; // already backing off

                // Threshold just crossed — pause the workdir watcher for a while
                var w = _workDirWatcher;
                if (w != null)
                {
                    try { w.EnableRaisingEvents = false; } catch { }
                    _stormRecoveryTimer?.Dispose();
                    _stormRecoveryTimer = new Timer(_ =>
                    {
                        lock (_lock)
                        {
                            if (_disposed) return;
                            _stormEventCount = 0;
                            _stormWindowStartTicks = MonotonicClock.NowMs();
                            try { w.EnableRaisingEvents = true; } catch { }
                        }
                        // One refresh to catch whatever happened while paused
                        Signal(RepoChangeKind.WorkingDir);
                    }, null, StormBackoffMs, Timeout.Infinite);
                }
                return true;
            }
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            // Buffer overflow or the directory became unavailable — recreate lazily
            lock (_lock)
            {
                if (_disposed) return;
                try
                {
                    if (ReferenceEquals(sender, _workDirWatcher))
                    {
                        _workDirWatcher?.Dispose();
                        _workDirWatcher = CreateWorkDirWatcher();
                    }
                    else if (ReferenceEquals(sender, _gitDirWatcher))
                    {
                        _gitDirWatcher?.Dispose();
                        _gitDirWatcher = CreateGitDirWatcher();
                    }
                }
                catch (Exception ex) { AppLog.Warn("FileSystemWatcher recreation failed", ex); }
            }
            // We may have missed events — request a refresh
            Signal(RepoChangeKind.Refs);
        }

        // ── Debounce + suppression ────────────────────────────────────────────

        private void Signal(RepoChangeKind kind)
        {
            lock (_lock)
            {
                if (_disposed) return;
                _hasPending = true;
                if (kind == RepoChangeKind.Refs) _pendingKind = RepoChangeKind.Refs;
                if (_suppressCount > 0)
                {
                    _pendingWhileSuppressed = true;
                    return;
                }
                _debounceTimer.Change(DebounceMs, Timeout.Infinite);
            }
        }

        private void OnDebounceElapsed(object state)
        {
            RepoChangeKind kind;
            lock (_lock)
            {
                if (_disposed || !_hasPending) return;
                if (_suppressCount > 0)
                {
                    // This timer was already scheduled before suppression started and is only now
                    // firing mid-operation. Signal() only sets _pendingWhileSuppressed for events
                    // that arrive *during* suppression — without setting it here too, EndSuppress()
                    // has nothing telling it to reschedule, and this pending change is silently
                    // lost until some unrelated future FS event happens to call Signal() again.
                    _pendingWhileSuppressed = true;
                    return;
                }
                kind = _pendingKind;
                _hasPending = false;
                _pendingKind = RepoChangeKind.WorkingDir;
            }
            try { Changed?.Invoke(kind); }
            catch (Exception ex) { AppLog.Warn("Repository change handler threw", ex); }
        }

        /// <summary>
        /// Suppresses change events for the duration of an app-initiated git
        /// operation. Events arriving while suppressed coalesce into a single
        /// event raised after the outermost scope is disposed.
        /// </summary>
        public IDisposable Suppress()
        {
            lock (_lock) { _suppressCount++; }
            return new SuppressScope(this);
        }

        private void EndSuppress()
        {
            lock (_lock)
            {
                _suppressCount = Math.Max(0, _suppressCount - 1);
                if (_suppressCount > 0) return;
                if (_pendingWhileSuppressed)
                {
                    _pendingWhileSuppressed = false;
                    _debounceTimer.Change(DebounceMs, Timeout.Infinite);
                }
            }
        }

        private sealed class SuppressScope : IDisposable
        {
            private RepositoryWatcher _owner;
            public SuppressScope(RepositoryWatcher owner) { _owner = owner; }
            public void Dispose()
            {
                var owner = Interlocked.Exchange(ref _owner, null);
                owner?.EndSuppress();
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _debounceTimer.Dispose();
            _stormRecoveryTimer?.Dispose();
            _workDirWatcher?.Dispose();
            _gitDirWatcher?.Dispose();
        }
    }

    internal static class MonotonicClock
    {
        // net472 has no Environment.TickCount64; wrap Stopwatch for a monotonic ms counter.
        private static readonly System.Diagnostics.Stopwatch s_clock = System.Diagnostics.Stopwatch.StartNew();
        public static long NowMs() => s_clock.ElapsedMilliseconds;
    }
}
