using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using PickleGit.ViewModels;

namespace PickleGit.Services
{
    /// <summary>A single action offered in the command palette (Ctrl+P).</summary>
    public sealed class PaletteCommand
    {
        public string Category { get; set; }
        public string Name { get; set; }
        public string Gesture { get; set; }
        public ICommand Command { get; set; }
        public object CommandParameter { get; set; }

        public string DisplayText => Category + ": " + Name;
    }

    /// <summary>
    /// Builds the flat list of commands shown in the command palette. Rebuilt each time the
    /// palette opens so it always reflects the current ActiveTab (per-repo commands disappear
    /// when no tab is open).
    /// </summary>
    public static class AppCommandRegistry
    {
        public static List<PaletteCommand> Build(AppViewModel app)
        {
            var list = new List<PaletteCommand>();
            void Add(string category, string name, ICommand command, string gesture = null, object param = null)
            {
                if (command == null) return;
                list.Add(new PaletteCommand
                {
                    Category = category,
                    Name = name,
                    Gesture = gesture,
                    Command = command,
                    CommandParameter = param
                });
            }

            Add("Repository", "Open Repository…", app.OpenRepoCommand, ShortcutManager.GetGesture("OpenRepo"));
            Add("Repository", "Clone Repository…", app.CloneRepoCommand, ShortcutManager.GetGesture("CloneRepo"));
            Add("Repository", "Initialize Repository…", app.InitRepoCommand);
            Add("Application", "Settings…", app.OpenSettingsCommand);
            Add("Application", "Command Palette", app.OpenCommandPaletteCommand, ShortcutManager.GetGesture("CommandPalette"));
            Add("Tabs", "Next Tab", app.NextTabCommand, ShortcutManager.GetGesture("NextTab"));
            Add("Tabs", "Previous Tab", app.PrevTabCommand, ShortcutManager.GetGesture("PrevTab"));
            Add("Tabs", "Close Current Tab", app.CloseTabCommand, ShortcutManager.GetGesture("CloseTab"), app.ActiveTab);

            var tab = app.ActiveTab;
            if (tab != null)
            {
                Add("Git", "Refresh", tab.RefreshCommand, ShortcutManager.GetGesture("Refresh"));
                Add("Git", "Fetch", tab.FetchCommand, ShortcutManager.GetGesture("Fetch"));
                Add("Git", "Fetch (Prune)", tab.FetchPruneCommand);
                Add("Git", "Fetch All Remotes", tab.FetchAllCommand);
                Add("Git", "Pull (Merge)", tab.PullCommand, ShortcutManager.GetGesture("Pull"));
                Add("Git", "Pull (Rebase)", tab.PullRebaseCommand);
                Add("Git", "Push", tab.PushCommand, ShortcutManager.GetGesture("Push"));
                Add("Git", "Force Push (With Lease)…", tab.ForcePushCommand);
                Add("Git", "Commit & Push", tab.CommitAndPushCommand);
                Add("Git", "Apply Patch File…", tab.ApplyPatchFileCommand);
                Add("Git", "Add Submodule…", tab.AddSubmoduleCommand);
                Add("Branch", "Create Branch…", tab.CreateBranchCommand, ShortcutManager.GetGesture("CreateBranch"));
                Add("Stash", "Stash Changes…", tab.StashCommand);
                Add("Stash", "Pop Latest Stash", tab.PopStashCommand);
                Add("View", "Show Staging Area", tab.ShowWorkingDirCommand);
                Add("Diff", "Next Hunk", tab.NextHunkCommand, ShortcutManager.GetGesture("NextHunk"));
                Add("Diff", "Previous Hunk", tab.PrevHunkCommand, ShortcutManager.GetGesture("PrevHunk"));
                Add("Edit", "Undo", tab.UndoCommand);
            }

            return list;
        }

        /// <summary>Simple case-insensitive subsequence-fuzzy filter over category + name.</summary>
        public static IEnumerable<PaletteCommand> Filter(IEnumerable<PaletteCommand> source, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return source;
            var q = query.Trim();
            return source
                .Select(c => new { c, score = FuzzyScore(c.DisplayText, q) })
                .Where(x => x.score >= 0)
                .OrderByDescending(x => x.score)
                .Select(x => x.c);
        }

        /// <summary>-1 if not a subsequence match; otherwise higher = better (contiguous/prefix matches win).</summary>
        private static int FuzzyScore(string text, string query)
        {
            var t = text.ToLowerInvariant();
            var q = query.ToLowerInvariant();
            if (t.Contains(q)) return t.StartsWith(q) ? 1000 - t.Length : 500 - t.Length;

            int ti = 0, qi = 0, score = 0, run = 0;
            while (ti < t.Length && qi < q.Length)
            {
                if (t[ti] == q[qi])
                {
                    qi++; run++; score += run;
                }
                else run = 0;
                ti++;
            }
            return qi == q.Length ? score : -1;
        }
    }
}
