using System.Collections.Generic;
using System.IO;
using PickleGit.Models;

namespace PickleGit.Services.Git
{
    /// <summary>
    /// Worktree domain logic extracted from RepositoryViewModel: parsing
    /// `git worktree list --porcelain` output and building the CLI arguments
    /// for add/remove. The VM keeps only the dialog + refresh orchestration.
    /// </summary>
    public static class WorktreeService
    {
        public static List<WorktreeInfo> ParsePorcelain(string text)
        {
            var list = new List<WorktreeInfo>();
            WorktreeInfo current = null;
            foreach (var rawLine in (text ?? string.Empty).Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (line.StartsWith("worktree "))
                {
                    current = new WorktreeInfo { Path = line.Substring(9) };
                    current.Name = Path.GetFileName(current.Path.TrimEnd('/', '\\'));
                    list.Add(current);
                }
                else if (current == null) continue;
                else if (line.StartsWith("branch "))
                {
                    var b = line.Substring(7);
                    current.Branch = b.StartsWith("refs/heads/") ? b.Substring(11) : b;
                }
                else if (line == "detached") current.Branch = "(detached)";
                else if (line == "locked" || line.StartsWith("locked ")) current.IsLocked = true;
            }
            if (list.Count > 0) list[0].IsMain = true; // `worktree list`'s first entry is always the main one
            return list;
        }

        /// <summary>An existing branch is checked out as-is; a name with no matching branch is created fresh (-b).</summary>
        public static string BuildAddArgs(string path, string branch, bool branchExists) => branchExists
            ? $"worktree add {CliGitService.Quote(path)} {CliGitService.Quote(branch)}"
            : $"worktree add -b {CliGitService.Quote(branch)} {CliGitService.Quote(path)}";

        public static string BuildRemoveArgs(string path)
            => $"worktree remove --force {CliGitService.Quote(path)}";
    }
}
