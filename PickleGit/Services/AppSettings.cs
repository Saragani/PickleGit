using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace PickleGit.Services
{
    internal static class AppSettings
    {
        // PICKLEGIT_APPDATA overrides the settings location — used by --test-instance runs
        // so UI automation never touches the real settings.json.
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetEnvironmentVariable("PICKLEGIT_APPDATA")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PickleGit"),
            "settings.json");

        private sealed class SettingsData
        {
            /// <summary>Schema version for future migrations.</summary>
            public int SettingsVersion { get; set; } = 1;

            /// <summary>Ceiling for the history walk; "Load more" doubles it per click at runtime.</summary>
            public int CommitLoadLimit { get; set; } = 10000;

            public List<string> OpenRepoPaths { get; set; } = new List<string>();
            public string ActiveRepoPath { get; set; }
            public string LastRepoPath { get; set; } // backward compat

            // Column visibility — defaults match "default columns" spec
            public bool ColBranchTag { get; set; } = true;
            public bool ColGraph { get; set; } = true;
            public bool ColCommitDesc { get; set; } = true;
            public bool ColAuthor { get; set; } = false;
            public bool ColDateTime { get; set; } = false;
            public bool ColSha { get; set; } = false;

            public bool SmartBranchVisibility { get; set; } = false;

            // Column widths
            public double ColWidthBranchTag  { get; set; } = 120;
            public double ColWidthGraph      { get; set; } = 150;
            public double ColWidthCommitDesc { get; set; } = 200;
            public double ColWidthAuthor     { get; set; } = 140;
            public double ColWidthDateTime   { get; set; } = 110;
            public double ColWidthSha        { get; set; } = 80;

            public Dictionary<string, List<string>> CollapsedBranchNodesByRepo { get; set; }
                = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            public bool SidebarLocalBranchesExpanded   { get; set; } = true;
            public bool SidebarRemoteBranchesExpanded  { get; set; } = true;
            public bool SidebarTagsExpanded            { get; set; } = false;
            public bool SidebarStashesExpanded         { get; set; } = false;
            public bool SidebarRemotesExpanded         { get; set; } = false;
            public bool SidebarReflogExpanded          { get; set; } = false;

            /// <summary>Applied as `git config --global rerere.enabled` when toggled.</summary>
            public bool RerereEnabled { get; set; } = false;

            /// <summary>When true, commits are made via the CLI with `-S` (or the repo's configured signer) instead of libgit2.</summary>
            public bool GpgSignCommits { get; set; } = false;

            /// <summary>User override for git.exe location, checked before PATH/Program Files auto-discovery.</summary>
            public string GitExePathOverride { get; set; }

            /// <summary>.NET date format string applied to the commit list's date/time column.</summary>
            public string DateFormat { get; set; } = "yyyy-MM-dd HH:mm";

            /// <summary>When false, "Discard changes" skips the confirmation dialog.</summary>
            public bool ConfirmBeforeDiscard { get; set; } = true;

            /// <summary>Commit-list dates as "2h ago" instead of the absolute DateFormat.</summary>
            public bool UseRelativeDates { get; set; } = false;

            /// <summary>Diff panes ignore whitespace-only changes (`git diff -w`; needs git.exe).</summary>
            public bool DiffIgnoreWhitespace { get; set; } = false;
            public bool DiffShowEntireFile { get; set; } = false;

            /// <summary>"Dark" or "Light" — the palette merged at startup (restart to apply).</summary>
            public string Theme { get; set; } = "Dark";

            /// <summary>Parent folder of the last successful clone — pre-fills the clone dialog.</summary>
            public string LastCloneParentDir { get; set; }

            /// <summary>Self-hosted provider mapping: domain → "github" or "gitlab"
            /// (GitHub Enterprise Server / self-managed GitLab).</summary>
            public Dictionary<string, string> HostingDomainKinds { get; set; }
                = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>Fallback author identity used when a repo has no git config and no per-repo override.</summary>
            public string DefaultAuthorName { get; set; }
            public string DefaultAuthorEmail { get; set; }

            /// <summary>Per-repo identity override, keyed by repo path.</summary>
            public Dictionary<string, string> RepoAuthorNames { get; set; }
                = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> RepoAuthorEmails { get; set; }
                = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>User-rebound keyboard shortcuts, keyed by ShortcutDescriptor.Id. A missing/empty
            /// entry means "use the default gesture".</summary>
            public Dictionary<string, string> ShortcutOverrides { get; set; }
                = new Dictionary<string, string>(StringComparer.Ordinal);

            // Main-window geometry (restored at startup; Maximized is the first-run default)
            public double WindowLeft { get; set; } = double.NaN;
            public double WindowTop { get; set; } = double.NaN;
            public double WindowWidth { get; set; }
            public double WindowHeight { get; set; }
            public bool WindowMaximized { get; set; } = true;
        }

        // ── Comprehensive save ────────────────────────────────────────────────

        public static void SaveAppState(
            IList<string> paths,
            string activePath,
            bool colBranchTag,
            bool colGraph,
            bool colCommitDesc,
            bool colAuthor,
            bool colDateTime,
            bool colSha,
            bool smartBranch,
            double wBranchTag  = 120,
            double wGraph      = 150,
            double wCommitDesc = 200,
            double wAuthor     = 140,
            double wDateTime   = 110,
            double wSha        = 80)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                var data = Load() ?? new SettingsData();
                data.OpenRepoPaths = paths.ToList();
                data.LastRepoPath = null;
                data.ActiveRepoPath = activePath;
                data.ColBranchTag = colBranchTag;
                data.ColGraph = colGraph;
                data.ColCommitDesc = colCommitDesc;
                data.ColAuthor = colAuthor;
                data.ColDateTime = colDateTime;
                data.ColSha = colSha;
                data.SmartBranchVisibility = smartBranch;
                data.ColWidthBranchTag  = wBranchTag;
                data.ColWidthGraph      = wGraph;
                data.ColWidthCommitDesc = wCommitDesc;
                data.ColWidthAuthor     = wAuthor;
                data.ColWidthDateTime   = wDateTime;
                data.ColWidthSha        = wSha;
                Save(data);
            }
            catch { }
        }

        // ── Load all settings ─────────────────────────────────────────────────

        public static List<string> LoadOpenRepos()
        {
            var data = Load() ?? new SettingsData();
            if ((data.OpenRepoPaths == null || data.OpenRepoPaths.Count == 0)
                && !string.IsNullOrEmpty(data.LastRepoPath))
                return new List<string> { data.LastRepoPath };
            return data.OpenRepoPaths ?? new List<string>();
        }

        public static string LoadActiveRepo() => (Load() ?? new SettingsData()).ActiveRepoPath;

        public static (bool branchTag, bool graph, bool commitDesc, bool author, bool dateTime, bool sha)
            LoadColumnSettings()
        {
            var d = Load() ?? new SettingsData();
            return (d.ColBranchTag, d.ColGraph, d.ColCommitDesc, d.ColAuthor, d.ColDateTime, d.ColSha);
        }

        public static bool LoadSmartBranchVisibility() => (Load() ?? new SettingsData()).SmartBranchVisibility;

        public static int LoadCommitLimit()
        {
            var limit = (Load() ?? new SettingsData()).CommitLoadLimit;
            return limit > 0 ? limit : 10000;
        }

        public static (double branchTag, double graph, double commitDesc, double author, double dateTime, double sha)
            LoadColumnWidths()
        {
            var d = Load() ?? new SettingsData();
            return (d.ColWidthBranchTag, d.ColWidthGraph, d.ColWidthCommitDesc, d.ColWidthAuthor, d.ColWidthDateTime, d.ColWidthSha);
        }

        public static HashSet<string> LoadCollapsedBranchNodes(string repoPath)
        {
            var d = Load() ?? new SettingsData();
            if (string.IsNullOrEmpty(repoPath) ||
                d.CollapsedBranchNodesByRepo == null ||
                !d.CollapsedBranchNodesByRepo.TryGetValue(repoPath, out var keys))
                return null;
            return new HashSet<string>(keys ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        }

        public static void SaveCollapsedBranchNodes(string repoPath, IEnumerable<string> keys)
        {
            if (string.IsNullOrEmpty(repoPath)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                var data = Load() ?? new SettingsData();
                if (data.CollapsedBranchNodesByRepo == null)
                    data.CollapsedBranchNodesByRepo = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                data.CollapsedBranchNodesByRepo[repoPath] = (keys ?? Enumerable.Empty<string>())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                Save(data);
            }
            catch { }
        }

        public static (bool local, bool remote, bool tags, bool stashes, bool remotes, bool reflog) LoadSidebarSectionStates()
        {
            var d = Load() ?? new SettingsData();
            return (d.SidebarLocalBranchesExpanded, d.SidebarRemoteBranchesExpanded,
                    d.SidebarTagsExpanded, d.SidebarStashesExpanded, d.SidebarRemotesExpanded,
                    d.SidebarReflogExpanded);
        }

        public static void SaveSidebarSectionState(string section, bool expanded)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                var data = Load() ?? new SettingsData();
                switch (section)
                {
                    case "local":   data.SidebarLocalBranchesExpanded  = expanded; break;
                    case "remote":  data.SidebarRemoteBranchesExpanded = expanded; break;
                    case "tags":    data.SidebarTagsExpanded           = expanded; break;
                    case "stashes": data.SidebarStashesExpanded        = expanded; break;
                    case "remotes": data.SidebarRemotesExpanded        = expanded; break;
                    case "reflog":  data.SidebarReflogExpanded         = expanded; break;
                }
                Save(data);
            }
            catch { }
        }

        public static bool LoadRerereEnabled() => (Load() ?? new SettingsData()).RerereEnabled;

        public static void SaveRerereEnabled(bool enabled)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                var data = Load() ?? new SettingsData();
                data.RerereEnabled = enabled;
                Save(data);
            }
            catch { }
        }

        public static bool LoadGpgSignCommits() => (Load() ?? new SettingsData()).GpgSignCommits;

        public static void SaveGpgSignCommits(bool enabled)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                var data = Load() ?? new SettingsData();
                data.GpgSignCommits = enabled;
                Save(data);
            }
            catch { }
        }

        public static string LoadGitExePathOverride() => (Load() ?? new SettingsData()).GitExePathOverride;

        public static void SaveGitExePathOverride(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                var data = Load() ?? new SettingsData();
                data.GitExePathOverride = path;
                Save(data);
            }
            catch { }
        }

        public static void SaveCommitLoadLimit(int limit)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                var data = Load() ?? new SettingsData();
                data.CommitLoadLimit = limit > 0 ? limit : 10000;
                Save(data);
            }
            catch { }
        }

        public static string LoadDateFormat()
        {
            var f = (Load() ?? new SettingsData()).DateFormat;
            return string.IsNullOrWhiteSpace(f) ? "yyyy-MM-dd HH:mm" : f;
        }

        public static void SaveDateFormat(string format)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                var data = Load() ?? new SettingsData();
                data.DateFormat = format;
                Save(data);
            }
            catch { }
        }

        public static string LoadHostingDomainKind(string domain)
        {
            if (string.IsNullOrEmpty(domain)) return null;
            var d = Load() ?? new SettingsData();
            return d.HostingDomainKinds != null && d.HostingDomainKinds.TryGetValue(domain, out var kind)
                ? kind : null;
        }

        public static Dictionary<string, string> LoadHostingDomainKinds() =>
            (Load() ?? new SettingsData()).HostingDomainKinds
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void SaveHostingDomainKind(string domain, string kind)
        {
            if (string.IsNullOrEmpty(domain)) return;
            var data = Load() ?? new SettingsData();
            if (data.HostingDomainKinds == null)
                data.HostingDomainKinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(kind)) data.HostingDomainKinds.Remove(domain);
            else data.HostingDomainKinds[domain] = kind;
            Save(data);
        }

        public static string LoadLastCloneParentDir() => (Load() ?? new SettingsData()).LastCloneParentDir;

        public static void SaveLastCloneParentDir(string dir)
        {
            var data = Load() ?? new SettingsData();
            data.LastCloneParentDir = dir;
            Save(data);
        }

        public static string LoadTheme()
        {
            var t = (Load() ?? new SettingsData()).Theme;
            return string.Equals(t, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
        }

        public static void SaveTheme(string theme)
        {
            var data = Load() ?? new SettingsData();
            data.Theme = theme;
            Save(data);
        }

        public static bool LoadDiffIgnoreWhitespace() => (Load() ?? new SettingsData()).DiffIgnoreWhitespace;

        public static void SaveDiffIgnoreWhitespace(bool enabled)
        {
            var data = Load() ?? new SettingsData();
            data.DiffIgnoreWhitespace = enabled;
            Save(data);
        }

        public static bool LoadDiffShowEntireFile() => (Load() ?? new SettingsData()).DiffShowEntireFile;

        public static void SaveDiffShowEntireFile(bool enabled)
        {
            var data = Load() ?? new SettingsData();
            data.DiffShowEntireFile = enabled;
            Save(data);
        }

        public static bool LoadUseRelativeDates() => (Load() ?? new SettingsData()).UseRelativeDates;

        public static void SaveUseRelativeDates(bool enabled)
        {
            var data = Load() ?? new SettingsData();
            data.UseRelativeDates = enabled;
            Save(data);
        }

        public static bool LoadConfirmBeforeDiscard() => (Load() ?? new SettingsData()).ConfirmBeforeDiscard;

        public static void SaveConfirmBeforeDiscard(bool enabled)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                var data = Load() ?? new SettingsData();
                data.ConfirmBeforeDiscard = enabled;
                Save(data);
            }
            catch { }
        }

        public static (string name, string email) LoadDefaultIdentity()
        {
            var d = Load() ?? new SettingsData();
            return (d.DefaultAuthorName, d.DefaultAuthorEmail);
        }

        public static void SaveDefaultIdentity(string name, string email)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                var data = Load() ?? new SettingsData();
                data.DefaultAuthorName = name;
                data.DefaultAuthorEmail = email;
                Save(data);
            }
            catch { }
        }

        public static (string name, string email) LoadRepoIdentity(string repoPath)
        {
            var d = Load() ?? new SettingsData();
            string name = null, email = null;
            d.RepoAuthorNames?.TryGetValue(repoPath ?? string.Empty, out name);
            d.RepoAuthorEmails?.TryGetValue(repoPath ?? string.Empty, out email);
            return (name, email);
        }

        public static void SaveRepoIdentity(string repoPath, string name, string email)
        {
            if (string.IsNullOrEmpty(repoPath)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                var data = Load() ?? new SettingsData();
                if (data.RepoAuthorNames == null) data.RepoAuthorNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (data.RepoAuthorEmails == null) data.RepoAuthorEmails = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (string.IsNullOrWhiteSpace(name)) data.RepoAuthorNames.Remove(repoPath); else data.RepoAuthorNames[repoPath] = name;
                if (string.IsNullOrWhiteSpace(email)) data.RepoAuthorEmails.Remove(repoPath); else data.RepoAuthorEmails[repoPath] = email;
                Save(data);
            }
            catch { }
        }

        public static Dictionary<string, string> LoadShortcutOverrides() =>
            (Load() ?? new SettingsData()).ShortcutOverrides ?? new Dictionary<string, string>(StringComparer.Ordinal);

        public static void SaveShortcutOverride(string id, string gesture)
        {
            if (string.IsNullOrEmpty(id)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                var data = Load() ?? new SettingsData();
                if (data.ShortcutOverrides == null) data.ShortcutOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
                if (string.IsNullOrWhiteSpace(gesture)) data.ShortcutOverrides.Remove(id);
                else data.ShortcutOverrides[id] = gesture;
                Save(data);
            }
            catch { }
        }

        public static (double left, double top, double width, double height, bool maximized) LoadWindowGeometry()
        {
            var d = Load() ?? new SettingsData();
            return (d.WindowLeft, d.WindowTop, d.WindowWidth, d.WindowHeight, d.WindowMaximized);
        }

        public static void SaveWindowGeometry(double left, double top, double width, double height, bool maximized)
        {
            var data = Load() ?? new SettingsData();
            data.WindowLeft = left;
            data.WindowTop = top;
            data.WindowWidth = width;
            data.WindowHeight = height;
            data.WindowMaximized = maximized;
            Save(data);
        }

        // ── Backward-compat helper ────────────────────────────────────────────

        public static void SaveOpenRepos(IList<string> paths)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                var data = Load() ?? new SettingsData();
                data.OpenRepoPaths = paths.ToList();
                data.LastRepoPath = null;
                Save(data);
            }
            catch { }
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private static readonly object Sync = new object();

        private static SettingsData Load()
        {
            try
            {
                lock (Sync)
                {
                    if (!File.Exists(SettingsPath)) return null;
                    return JsonConvert.DeserializeObject<SettingsData>(File.ReadAllText(SettingsPath));
                }
            }
            catch (Exception ex) { AppLog.Warn("Settings load failed", ex); return null; }
        }

        /// <summary>Atomic write: serialize to a temp file, then swap it in with File.Replace
        /// so a crash mid-write can never leave a truncated settings.json.</summary>
        private static void Save(SettingsData data)
        {
            try
            {
                var json = JsonConvert.SerializeObject(data, Formatting.Indented);
                lock (Sync)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                    var tmp = SettingsPath + ".tmp";
                    File.WriteAllText(tmp, json);
                    if (File.Exists(SettingsPath))
                        File.Replace(tmp, SettingsPath, null);
                    else
                        File.Move(tmp, SettingsPath);
                }
            }
            catch (Exception ex) { AppLog.Error("Settings save failed", ex); }
        }
    }
}
