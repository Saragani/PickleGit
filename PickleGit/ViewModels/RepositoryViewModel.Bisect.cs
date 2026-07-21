using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using PickleGit.Models;
using PickleGit.Services;
using PickleGit.Services.Git;

namespace PickleGit.ViewModels
{
    /// <summary>git bisect: right-click a commit to mark it bad, then another to mark it good and
    /// start the session; Good/Bad/Skip the checked-out midpoint each step until git narrows to the
    /// first bad commit. Unlike every other CLI-driven operation in this app (one dialog, one CLI
    /// call, done-or-paused-for-conflicts), bisect is a genuinely multi-step session — N sequential
    /// round-trips — so it needs its own banner that persists across all of them.</summary>
    public partial class RepositoryViewModel
    {
        private BisectState _bisectInfo = new BisectState();
        public BisectState BisectInfo
        {
            get => _bisectInfo;
            private set
            {
                if (!Set(ref _bisectInfo, value)) return;
                RaisePropertyChanged(nameof(HasBisect));
                RaiseDetailPanelPropertiesChanged();
            }
        }
        public bool HasBisect => _bisectInfo?.InProgress == true;
        public bool CanStartBisect => HasRepo && !HasConflict && !HasBisect;

        private string _pendingBisectBadSha;
        private bool _isBisectPending;
        /// <summary>True after "Start bisect: mark bad" is clicked on a commit, until a second
        /// commit is marked good (a repeated "mark bad" click just overwrites the pending pick).</summary>
        public bool IsBisectPending { get => _isBisectPending; private set => Set(ref _isBisectPending, value); }

        public ICommand MarkBisectBadCommand { get; private set; }
        public ICommand MarkBisectGoodCommand { get; private set; }
        public ICommand BisectGoodCommand { get; private set; }
        public ICommand BisectBadCommand { get; private set; }
        public ICommand BisectSkipCommand { get; private set; }
        public ICommand BisectResetCommand { get; private set; }

        // git's actual output quotes "bad" (and "good", for a bisect run in reverse/old-new mode):
        // "<sha> is the first 'bad' commit" — verified against real git.exe stdout.
        private static readonly Regex BisectFoundRegex =
            new Regex(@"^(?<sha>[0-9a-f]{40}) is the first '(bad|good)' commit", RegexOptions.Multiline);
        private static readonly Regex BisectProgressRegex =
            new Regex(@"Bisecting:\s+(?<rev>\d+)\s+revisions?\s+left to test after this \(roughly (?<steps>\d+)\s+steps?\)");

        private void InitializeBisectCommands()
        {
            MarkBisectBadCommand = new RelayCommand(
                _ => MarkBisectBad(),
                _ => CanStartBisect && SelectedNode?.Commit?.IsUncommitted != true);
            MarkBisectGoodCommand = new RelayCommand(
                _ => _ = StartBisectAsync(),
                _ => IsBisectPending && SelectedNode?.Commit?.IsUncommitted != true);
            BisectGoodCommand = new RelayCommand(() => _ = BisectStepAsync("good"), () => HasBisect && !BisectInfo.Found);
            BisectBadCommand = new RelayCommand(() => _ = BisectStepAsync("bad"), () => HasBisect && !BisectInfo.Found);
            BisectSkipCommand = new RelayCommand(() => _ = BisectStepAsync("skip"), () => HasBisect && !BisectInfo.Found);
            BisectResetCommand = new RelayCommand(() => _ = BisectResetAsync(), () => HasBisect);
        }

        private void MarkBisectBad()
        {
            var sha = SelectedNode?.Commit?.Sha;
            if (sha == null) return;
            _pendingBisectBadSha = sha;
            IsBisectPending = true;
        }

        private Task StartBisectAsync()
        {
            var goodSha = SelectedNode?.Commit?.Sha;
            var badSha = _pendingBisectBadSha;
            _pendingBisectBadSha = null;
            IsBisectPending = false;
            if (goodSha == null || badSha == null) return Task.CompletedTask;
            return RunBisectCliAsync("Starting bisect…",
                $"bisect start {CliGitService.Quote(badSha)} {CliGitService.Quote(goodSha)}");
        }

        private Task BisectStepAsync(string verb) => RunBisectCliAsync($"Bisect: marking {verb}…", $"bisect {verb}");

        private Task BisectResetAsync() => RunBisectCliAsync("Resetting bisect…", "bisect reset");

        /// <summary>Runs a bisect CLI step and captures stdout directly. Unlike RunCliAsync/
        /// RunCliAllowingConflictAsync (RepositoryViewModel.Remote.cs), which route through
        /// RunAsync(string, Action) and only ever surface a bool, bisect needs the actual stdout
        /// text to detect completion ("&lt;sha&gt; is the first bad commit") and progress
        /// ("Bisecting: N revisions left…") — neither existing helper exposes it.</summary>
        private async Task RunBisectCliAsync(string status, string args)
        {
            if (_git.Cli == null || !_git.Cli.IsAvailable)
            {
                DialogService.ShowError("Bisect",
                    "This feature requires Git for Windows (git.exe), which was not found on this machine. " +
                    "Install it from https://git-scm.com and try again.");
                return;
            }
            GitCliResult result = null;
            var ok = await RunAsync(status, () =>
            {
                result = _git.Cli.RunAsync(args, new GitCliOptions { Progress = new Progress<string>(ReportProgress) }, OpToken)
                    .GetAwaiter().GetResult();
                _git.Reopen();
                // A skip/good/bad that lands on a real merge conflict is legitimate, not an error —
                // same "stopped for a recognized reason" check RunCliAllowingConflictAsync uses.
                if (!result.Success && !_git.GetConflictState().HasConflicts)
                    throw new InvalidOperationException(result.ErrorText);
            });
            if (!ok) { await RefreshAsync(); return; }

            var stdout = result?.StdOut ?? string.Empty;
            var updated = await _git.Executor.RunAsync(() => _git.GetBisectState());

            var foundMatch = BisectFoundRegex.Match(stdout);
            if (foundMatch.Success)
            {
                updated.Found = true;
                updated.FirstBadSha = foundMatch.Groups["sha"].Value;
                updated.FirstBadSummary = ExtractFirstBadSummary(stdout, foundMatch);
            }
            var progressMatch = BisectProgressRegex.Match(stdout);
            if (progressMatch.Success)
            {
                updated.RevisionsLeft = int.Parse(progressMatch.Groups["rev"].Value);
                updated.StepsRemaining = int.Parse(progressMatch.Groups["steps"].Value);
            }
            BisectInfo = updated;
            await LoadWorkingDirAsync();
            await RefreshAsync();

            if (foundMatch.Success)
            {
                var summary = string.IsNullOrEmpty(updated.FirstBadSummary) ? string.Empty : "\n" + updated.FirstBadSummary;
                if (DialogService.Confirm("Bisect Complete",
                        $"First bad commit: {updated.FirstBadSha.Substring(0, 7)}{summary}\n\n" +
                        "Reset now to return to your original branch, or keep exploring this commit.",
                        "Reset now"))
                {
                    await RunBisectCliAsync("Resetting bisect…", "bisect reset");
                }
            }
        }

        // Matches ONLY the raw `git show`-style header line ("commit <40-hex-char-sha>"), not an
        // actual commit message that happens to start with the word "commit" (e.g. a test repo
        // whose messages are literally "commit 1", "commit 2", ... — which a plain
        // StartsWith("commit ") check would wrongly swallow too).
        private static readonly Regex CommitHeaderLineRegex = new Regex(@"^commit [0-9a-f]{40}$");

        /// <summary>Skips past the "commit &lt;sha&gt;"/"Author:"/"Date:" header lines `git bisect`
        /// prints after the found-commit line (a `git show`-style header) to reach the actual
        /// commit message subject.</summary>
        private static string ExtractFirstBadSummary(string stdout, Match foundMatch)
        {
            var rest = stdout.Substring(foundMatch.Index + foundMatch.Length);
            foreach (var raw in rest.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (CommitHeaderLineRegex.IsMatch(line)) continue;
                if (line.StartsWith("Author:", StringComparison.Ordinal)) continue;
                if (line.StartsWith("Date:", StringComparison.Ordinal)) continue;
                return line;
            }
            return null;
        }
    }
}
