using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace PickleGit.Services
{
    /// <summary>Describes one rebindable keyboard shortcut: a stable Id, a display name for the
    /// Settings UI and command palette, the default gesture text (KeyGestureConverter format,
    /// e.g. "Ctrl+Shift+P"), and the binding path(s) MainWindow resolves the command/parameter from.</summary>
    public class ShortcutDescriptor
    {
        public string Id;
        public string DisplayName;
        public string DefaultGesture;
        public string CommandPath;
        public string CommandParameterPath;
    }

    /// <summary>
    /// Single source of truth for the app's keyboard shortcuts. MainWindow builds its
    /// InputBindings from this list at startup and whenever a gesture is changed in Settings;
    /// AppCommandRegistry uses the same list to show the current gesture next to each palette entry.
    /// </summary>
    public static class ShortcutManager
    {
        public static readonly List<ShortcutDescriptor> Actions = new List<ShortcutDescriptor>
        {
            new ShortcutDescriptor { Id = "Refresh",        DisplayName = "Refresh",              DefaultGesture = "F5",             CommandPath = "ActiveTab.RefreshCommand" },
            new ShortcutDescriptor { Id = "OpenRepo",       DisplayName = "Open Repository",       DefaultGesture = "Ctrl+O",         CommandPath = "OpenRepoCommand" },
            new ShortcutDescriptor { Id = "CloneRepo",      DisplayName = "Clone Repository",      DefaultGesture = "Ctrl+Shift+N",   CommandPath = "CloneRepoCommand" },
            new ShortcutDescriptor { Id = "CloseTab",       DisplayName = "Close Tab",             DefaultGesture = "Ctrl+W",         CommandPath = "CloseTabCommand", CommandParameterPath = "ActiveTab" },
            new ShortcutDescriptor { Id = "NextTab",        DisplayName = "Next Tab",              DefaultGesture = "Ctrl+Tab",       CommandPath = "NextTabCommand" },
            new ShortcutDescriptor { Id = "PrevTab",        DisplayName = "Previous Tab",          DefaultGesture = "Ctrl+Shift+Tab", CommandPath = "PrevTabCommand" },
            new ShortcutDescriptor { Id = "Push",           DisplayName = "Push",                  DefaultGesture = "Ctrl+Shift+P",   CommandPath = "ActiveTab.PushCommand" },
            new ShortcutDescriptor { Id = "Pull",           DisplayName = "Pull",                  DefaultGesture = "Ctrl+Shift+L",   CommandPath = "ActiveTab.PullCommand" },
            new ShortcutDescriptor { Id = "Fetch",          DisplayName = "Fetch",                 DefaultGesture = "Ctrl+Shift+F",   CommandPath = "ActiveTab.FetchCommand" },
            new ShortcutDescriptor { Id = "CreateBranch",   DisplayName = "Create Branch",         DefaultGesture = "Ctrl+B",         CommandPath = "ActiveTab.CreateBranchCommand" },
            new ShortcutDescriptor { Id = "NextHunk",       DisplayName = "Next Hunk",             DefaultGesture = "F7",             CommandPath = "ActiveTab.NextHunkCommand" },
            new ShortcutDescriptor { Id = "PrevHunk",       DisplayName = "Previous Hunk",         DefaultGesture = "Shift+F7",       CommandPath = "ActiveTab.PrevHunkCommand" },
            new ShortcutDescriptor { Id = "CommandPalette", DisplayName = "Command Palette",       DefaultGesture = "Ctrl+P",         CommandPath = "OpenCommandPaletteCommand" },
            new ShortcutDescriptor { Id = "FindCommits",    DisplayName = "Find Commits",          DefaultGesture = "Ctrl+F",         CommandPath = "FocusCommitSearchCommand" },
            new ShortcutDescriptor { Id = "FocusCommits",   DisplayName = "Focus Commit List",     DefaultGesture = "Ctrl+1",         CommandPath = "FocusCommitListCommand" },
        };

        /// <summary>The effective gesture text for an action — its saved override, or its default.</summary>
        public static string GetGesture(string id)
        {
            var overrides = AppSettings.LoadShortcutOverrides();
            if (overrides.TryGetValue(id, out var g) && !string.IsNullOrWhiteSpace(g)) return g;
            return Actions.FirstOrDefault(a => a.Id == id)?.DefaultGesture;
        }

        public static void SetGesture(string id, string gesture) => AppSettings.SaveShortcutOverride(id, gesture);

        public static void ResetGesture(string id) => AppSettings.SaveShortcutOverride(id, null);

        /// <summary>Returns the display name of another action already bound to <paramref name="gesture"/>,
        /// or null if it's free. Compares parsed Key+Modifiers rather than raw text, so gestures
        /// written differently but meaning the same combination still collide. Without this check,
        /// MainWindow ends up with two InputBindings sharing one KeyGesture and WPF's tie-break
        /// order between them is unpredictable — the rebound shortcut can silently fire the wrong
        /// action.</summary>
        public static string FindConflict(string gesture, string excludingId)
        {
            if (string.IsNullOrWhiteSpace(gesture)) return null;
            if (!(TryParseGesture(gesture) is KeyGesture parsed)) return null;

            foreach (var action in Actions)
            {
                if (action.Id == excludingId) continue;
                var other = GetGesture(action.Id);
                if (!(TryParseGesture(other) is KeyGesture otherParsed)) continue;
                if (otherParsed.Key == parsed.Key && otherParsed.Modifiers == parsed.Modifiers)
                    return action.DisplayName;
            }
            return null;
        }

        private static KeyGesture TryParseGesture(string gesture)
        {
            if (string.IsNullOrWhiteSpace(gesture)) return null;
            try { return new KeyGestureConverter().ConvertFromString(gesture) as KeyGesture; }
            catch { return null; }
        }
    }
}
