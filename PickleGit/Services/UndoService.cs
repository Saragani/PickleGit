using System;
using System.Linq;
using PickleGit.Models;

namespace PickleGit.Services
{
    /// <summary>
    /// Reverses the last mutating operation recorded by RepositoryViewModel.
    /// Runs on GitService.Executor — safe to throw synchronously; the caller (RunAsync)
    /// surfaces the exception as an error dialog. Never blocks on the UI thread.
    /// </summary>
    public static class UndoService
    {
        public static void Undo(GitService git, UndoEntry entry)
        {
            switch (entry.Kind)
            {
                case UndoKind.Commit:
                    EnsureHeadUnchanged(git, entry);
                    git.ResetTo(entry.PreHeadSha, "soft");
                    break;

                case UndoKind.HeadHardMove:
                    EnsureHeadUnchanged(git, entry);
                    git.ResetTo(entry.PreHeadSha, "hard");
                    break;

                case UndoKind.Checkout:
                    EnsureHeadUnchanged(git, entry);
                    if (!string.IsNullOrEmpty(entry.BranchName))
                        git.Checkout(entry.BranchName);
                    else
                        git.CheckoutCommit(entry.PreHeadSha);
                    break;

                case UndoKind.BranchDelete:
                    if (git.GetBranches().Any(b => !b.IsRemote &&
                            string.Equals(b.Name, entry.BranchName, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException(
                            $"Cannot undo — a branch named '{entry.BranchName}' already exists.");
                    git.CreateBranch(entry.BranchName, entry.Sha);
                    break;
            }
        }

        private static void EnsureHeadUnchanged(GitService git, UndoEntry entry)
        {
            var current = git.GetHeadSha();
            if (!string.Equals(current, entry.PostHeadSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Cannot undo — the repository has changed since this operation.");
        }
    }
}
