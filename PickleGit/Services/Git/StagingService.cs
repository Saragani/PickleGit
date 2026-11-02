using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using PickleGit.Models;

namespace PickleGit.Services.Git
{
    /// <summary>Which index/worktree mutation a hunk- or line-level patch performs.
    /// Maps 1:1 to the `git apply` flag combination in <see cref="StagingService.ApplyArgs"/>.</summary>
    public enum StagingPatchOp
    {
        Stage,   // apply --cached
        Unstage, // apply --cached --reverse
        Discard  // apply --reverse (worktree)
    }

    /// <summary>
    /// Staging domain logic extracted from RepositoryViewModel: whole-file index
    /// operations (executor-routed), target resolution for multi-select actions,
    /// and hunk/line patch construction. The VM keeps dialogs, watcher suppression
    /// and post-op refresh orchestration.
    /// </summary>
    public class StagingService
    {
        private readonly GitService _git;

        public StagingService(GitService git) { _git = git; }

        public Task StageAsync(IEnumerable<string> paths)
            => _git.Executor.RunAsync(() => _git.StageFiles(paths));

        public Task UnstageAsync(IEnumerable<string> paths)
            => _git.Executor.RunAsync(() => _git.UnstageFiles(paths));

        /// <summary>
        /// Resolves what a Stage/Unstage/Discard action should apply to: the full multi-selection when the
        /// clicked/right-clicked item is part of it (or no specific item was passed, e.g. a keyboard/menu
        /// invocation), otherwise just the single item that was clicked. Per-row buttons don't change ListView
        /// selection on click, so a stale or unrelated selection safely falls back to the single clicked file.
        /// </summary>
        public static List<FileChange> ResolveTargets(ObservableCollection<FileChange> selected, object param)
        {
            // Tree view: a right-clicked row arrives as a FileTreeRow. A folder row stands for every
            // file beneath it; when all of those are already part of the current selection, the action
            // applies to the whole selection (same rule as a clicked file inside a multi-selection).
            if (param is FileTreeRow row)
            {
                var rowFiles = row.Kind == FileTreeRowKind.Folder
                    ? (row.Files ?? new List<FileChange>())
                    : (row.File != null ? new List<FileChange> { row.File } : new List<FileChange>());
                if (selected != null && selected.Count > rowFiles.Count && rowFiles.Count > 0 && rowFiles.All(selected.Contains))
                    return selected.ToList();
                return rowFiles.ToList();
            }

            var clicked = param as FileChange;
            if (selected != null && selected.Count > 1 && (clicked == null || selected.Contains(clicked)))
                return selected.ToList();
            if (clicked != null) return new List<FileChange> { clicked };
            return selected != null ? selected.ToList() : new List<FileChange>();
        }

        /// <summary>The `git apply` invocation (reading the patch from stdin) for the given operation.</summary>
        public static string ApplyArgs(StagingPatchOp op)
        {
            switch (op)
            {
                case StagingPatchOp.Stage: return "apply --cached -";
                case StagingPatchOp.Unstage: return "apply --cached --reverse -";
                default: return "apply --reverse -";
            }
        }

        public static string BuildHunkPatch(FileChange file, DiffHunk hunk)
            => PatchBuilder.BuildPatch(file.Path, new[] { hunk }, file.Kind);

        public static string BuildLinePatch(FileChange file, List<(DiffHunk Hunk, HashSet<DiffLine> Selected)> groups, StagingPatchOp op)
            => PatchBuilder.BuildLinePatch(file.Path, groups, file.Kind, reverseTarget: op != StagingPatchOp.Stage);
    }
}
