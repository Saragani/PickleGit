using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PickleGit.Services;
using PickleGit.Services.Git;

namespace PickleGit.ViewModels
{
    public class AppViewModel : BaseViewModel
    {
        public const double MinColWidthBranchTag  = 50;
        public const double MinColWidthGraph      = 60;
        public const double MinColWidthCommitDesc = 80;
        public const double MinColWidthAuthor     = 50;
        public const double MinColWidthDateTime   = 50;
        public const double MinColWidthSha        = 40;

        public ObservableCollection<RepositoryViewModel> Tabs { get; }
            = new ObservableCollection<RepositoryViewModel>();

        private RepositoryViewModel _activeTab;
        public RepositoryViewModel ActiveTab
        {
            get => _activeTab;
            set
            {
                if (Set(ref _activeTab, value))
                {
                    if (value != null && !value.IsLoaded)
                        _ = value.EnsureLoadedAsync();
                    SaveSettings();
                }
            }
        }

        // ── Column visibility ─────────────────────────────────────────────────

        private bool _showBranchTag;
        private bool _showGraph;
        private bool _showCommitDesc;
        private bool _showAuthor;
        private bool _showDateTime;
        private bool _showSha;

        private double _colWidthBranchTag  = 120;
        private double _colWidthGraph      = 150;
        private double _colWidthCommitDesc = 200;
        private double _colWidthAuthor     = 140;
        private double _colWidthDateTime   = 110;
        private double _colWidthSha        = 80;
        private double _commitListViewportWidth;

        private bool _sidebarLocalBranchesExpanded  = true;
        private bool _sidebarRemoteBranchesExpanded = true;
        private bool _sidebarTagsExpanded           = false;
        private bool _sidebarStashesExpanded        = false;
        private bool _sidebarRemotesExpanded        = false;
        private bool _sidebarReflogExpanded         = false;

        public bool ShowBranchTag  { get => _showBranchTag;  set { if (Set(ref _showBranchTag,  value)) OnColumnChanged(); } }
        public bool ShowGraph      { get => _showGraph;       set { if (Set(ref _showGraph,      value)) OnColumnChanged(); } }
        public bool ShowCommitDesc { get => _showCommitDesc;  set { if (Set(ref _showCommitDesc, value)) OnColumnChanged(); } }
        public bool ShowAuthor     { get => _showAuthor;      set { if (Set(ref _showAuthor,     value)) OnColumnChanged(); } }
        public bool ShowDateTime   { get => _showDateTime;    set { if (Set(ref _showDateTime,   value)) OnColumnChanged(); } }
        public bool ShowSha        { get => _showSha;         set { if (Set(ref _showSha,        value)) OnColumnChanged(); } }

        public int VisibleColumnCount =>
            (ShowBranchTag  ? 1 : 0) + (ShowGraph      ? 1 : 0) + (ShowCommitDesc ? 1 : 0) +
            (ShowAuthor     ? 1 : 0) + (ShowDateTime   ? 1 : 0) + (ShowSha        ? 1 : 0);

        public double ColWidthBranchTag  { get => _colWidthBranchTag;  set { if (Set(ref _colWidthBranchTag,  Math.Max(MinColWidthBranchTag, value))) OnColumnWidthChanged(); } }
        public double ColWidthGraph      { get => _colWidthGraph;      set { if (Set(ref _colWidthGraph,      Math.Max(MinColWidthGraph, value))) OnColumnWidthChanged(); } }
        public double ColWidthCommitDesc { get => _colWidthCommitDesc; set { if (Set(ref _colWidthCommitDesc, Math.Max(MinColWidthCommitDesc, value))) OnColumnWidthChanged(); } }
        public double ColWidthAuthor     { get => _colWidthAuthor;     set { if (Set(ref _colWidthAuthor,     Math.Max(MinColWidthAuthor, value))) OnColumnWidthChanged(); } }
        public double ColWidthDateTime   { get => _colWidthDateTime;   set { if (Set(ref _colWidthDateTime,   Math.Max(MinColWidthDateTime, value))) OnColumnWidthChanged(); } }
        public double ColWidthSha        { get => _colWidthSha;        set { if (Set(ref _colWidthSha,        Math.Max(MinColWidthSha, value))) OnColumnWidthChanged(); } }

        public double CommitListViewportWidth
        {
            get => _commitListViewportWidth;
            set
            {
                var normalized = Math.Max(0, value);
                if (Math.Abs(_commitListViewportWidth - normalized) < 0.5)
                    return;

                _commitListViewportWidth = normalized;
                RaiseEffectiveColumnWidthPropertiesChanged();
            }
        }

        public bool SidebarLocalBranchesExpanded
        {
            get => _sidebarLocalBranchesExpanded;
            set { if (Set(ref _sidebarLocalBranchesExpanded, value)) AppSettings.SaveSidebarSectionState("local", value); }
        }
        public bool SidebarRemoteBranchesExpanded
        {
            get => _sidebarRemoteBranchesExpanded;
            set { if (Set(ref _sidebarRemoteBranchesExpanded, value)) AppSettings.SaveSidebarSectionState("remote", value); }
        }
        public bool SidebarTagsExpanded
        {
            get => _sidebarTagsExpanded;
            set { if (Set(ref _sidebarTagsExpanded, value)) AppSettings.SaveSidebarSectionState("tags", value); }
        }
        public bool SidebarStashesExpanded
        {
            get => _sidebarStashesExpanded;
            set { if (Set(ref _sidebarStashesExpanded, value)) AppSettings.SaveSidebarSectionState("stashes", value); }
        }
        public bool SidebarRemotesExpanded
        {
            get => _sidebarRemotesExpanded;
            set { if (Set(ref _sidebarRemotesExpanded, value)) AppSettings.SaveSidebarSectionState("remotes", value); }
        }
        public bool SidebarReflogExpanded
        {
            get => _sidebarReflogExpanded;
            set { if (Set(ref _sidebarReflogExpanded, value)) AppSettings.SaveSidebarSectionState("reflog", value); }
        }

        public GridLength EffectiveColWidthBranchTag  => GetEffectiveColumnWidth(0);
        public GridLength EffectiveColWidthGraph      => GetEffectiveColumnWidth(1);
        public GridLength EffectiveColWidthCommitDesc => GetEffectiveColumnWidth(2);
        public GridLength EffectiveColWidthAuthor     => GetEffectiveColumnWidth(3);
        public GridLength EffectiveColWidthDateTime   => GetEffectiveColumnWidth(4);
        public GridLength EffectiveColWidthSha        => GetEffectiveColumnWidth(5);

        public bool CanToggleBranchTag  => VisibleColumnCount > 1 || !ShowBranchTag;
        public bool CanToggleGraph      => VisibleColumnCount > 1 || !ShowGraph;
        public bool CanToggleCommitDesc => VisibleColumnCount > 1 || !ShowCommitDesc;
        public bool CanToggleAuthor     => VisibleColumnCount > 1 || !ShowAuthor;
        public bool CanToggleDateTime   => VisibleColumnCount > 1 || !ShowDateTime;
        public bool CanToggleSha        => VisibleColumnCount > 1 || !ShowSha;

        private void OnColumnChanged()
        {
            RaisePropertyChanged(nameof(VisibleColumnCount));
            RaisePropertyChanged(nameof(CanToggleBranchTag));
            RaisePropertyChanged(nameof(CanToggleGraph));
            RaisePropertyChanged(nameof(CanToggleCommitDesc));
            RaisePropertyChanged(nameof(CanToggleAuthor));
            RaisePropertyChanged(nameof(CanToggleDateTime));
            RaisePropertyChanged(nameof(CanToggleSha));
            RaiseEffectiveColumnWidthPropertiesChanged();
            SaveSettings();
        }

        private void OnColumnWidthChanged()
        {
            RaiseEffectiveColumnWidthPropertiesChanged();
            SaveSettings();
        }

        private void RaiseEffectiveColumnWidthPropertiesChanged()
        {
            RaisePropertyChanged(nameof(EffectiveColWidthBranchTag));
            RaisePropertyChanged(nameof(EffectiveColWidthGraph));
            RaisePropertyChanged(nameof(EffectiveColWidthCommitDesc));
            RaisePropertyChanged(nameof(EffectiveColWidthAuthor));
            RaisePropertyChanged(nameof(EffectiveColWidthDateTime));
            RaisePropertyChanged(nameof(EffectiveColWidthSha));
        }

        private GridLength GetEffectiveColumnWidth(int column)
        {
            if (!IsColumnVisible(column))
                return new GridLength(0);

            var width = column == GetLastVisibleColumn()
                ? GetFillWidthForLastVisibleColumn(column)
                : GetSavedColumnWidth(column);

            return new GridLength(Math.Max(GetMinColumnWidth(column), width));
        }

        private double GetFillWidthForLastVisibleColumn(int column)
        {
            if (_commitListViewportWidth <= 0)
                return GetSavedColumnWidth(column);

            double otherVisibleWidth = 0;
            for (int i = 0; i < 6; i++)
            {
                if (i != column && IsColumnVisible(i))
                    otherVisibleWidth += Math.Max(GetMinColumnWidth(i), GetSavedColumnWidth(i));
            }

            return _commitListViewportWidth - otherVisibleWidth;
        }

        private int GetLastVisibleColumn()
        {
            for (int i = 5; i >= 0; i--)
            {
                if (IsColumnVisible(i))
                    return i;
            }

            return -1;
        }

        private bool IsColumnVisible(int column)
        {
            switch (column)
            {
                case 0: return ShowBranchTag;
                case 1: return ShowGraph;
                case 2: return ShowCommitDesc;
                case 3: return ShowAuthor;
                case 4: return ShowDateTime;
                case 5: return ShowSha;
                default: return false;
            }
        }

        private double GetSavedColumnWidth(int column)
        {
            switch (column)
            {
                case 0: return ColWidthBranchTag;
                case 1: return ColWidthGraph;
                case 2: return ColWidthCommitDesc;
                case 3: return ColWidthAuthor;
                case 4: return ColWidthDateTime;
                case 5: return ColWidthSha;
                default: return 0;
            }
        }

        private double GetMinColumnWidth(int column)
        {
            switch (column)
            {
                case 0: return MinColWidthBranchTag;
                case 1: return MinColWidthGraph;
                case 2: return MinColWidthCommitDesc;
                case 3: return MinColWidthAuthor;
                case 4: return MinColWidthDateTime;
                case 5: return MinColWidthSha;
                default: return 0;
            }
        }

        // ── Smart branch visibility ───────────────────────────────────────────

        private bool _smartBranchVisibility;
        public bool SmartBranchVisibility
        {
            get => _smartBranchVisibility;
            set
            {
                if (Set(ref _smartBranchVisibility, value))
                {
                    foreach (var tab in Tabs)
                        tab.SmartBranchVisibility = value;
                    SaveSettings();
                }
            }
        }

        // ── rerere (Phase 2 opt-in — no dedicated Settings window yet) ────────

        private bool _rerereEnabled;
        public bool RerereEnabled
        {
            get => _rerereEnabled;
            set
            {
                if (Set(ref _rerereEnabled, value))
                {
                    AppSettings.SaveRerereEnabled(value);
                    _ = ApplyRerereSettingAsync(value);
                }
            }
        }

        private static async Task ApplyRerereSettingAsync(bool enabled)
        {
            if (!GitCli.IsGitAvailable) return;
            try
            {
                var workDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                await GitCli.RunAsync(workDir, $"config --global rerere.enabled {(enabled ? "true" : "false")}");
            }
            catch { }
        }

        // ── GPG commit signing (Phase 5 — no dedicated Settings window yet) ────

        private bool _gpgSignCommits;
        public bool GpgSignCommits
        {
            get => _gpgSignCommits;
            set
            {
                if (Set(ref _gpgSignCommits, value))
                    AppSettings.SaveGpgSignCommits(value);
            }
        }

        // ── General settings (Settings window) ──────────────────────────────────

        private string _gitExePathOverride;
        public string GitExePathOverride
        {
            get => _gitExePathOverride;
            set
            {
                if (Set(ref _gitExePathOverride, value))
                {
                    AppSettings.SaveGitExePathOverride(value);
                    GitCli.GitPathOverride = value;
                    GitCli.InvalidateDiscovery();
                }
            }
        }

        private int _defaultCommitLoadLimit;
        public int DefaultCommitLoadLimit
        {
            get => _defaultCommitLoadLimit;
            set
            {
                var v = value > 0 ? value : 10000;
                if (Set(ref _defaultCommitLoadLimit, v))
                    AppSettings.SaveCommitLoadLimit(v);
            }
        }

        // ── UI preferences (Settings window) ────────────────────────────────────

        private string _dateFormat;
        public string DateFormat
        {
            get => _dateFormat;
            set
            {
                if (Set(ref _dateFormat, value))
                {
                    Converters.DateFormatConverter.CurrentFormat = value;
                    AppSettings.SaveDateFormat(value);
                }
            }
        }

        private bool _useRelativeDates;
        public bool UseRelativeDates
        {
            get => _useRelativeDates;
            set
            {
                if (Set(ref _useRelativeDates, value))
                {
                    Converters.DateFormatConverter.UseRelative = value;
                    AppSettings.SaveUseRelativeDates(value);
                }
            }
        }

        private bool _confirmBeforeDiscard;
        public bool ConfirmBeforeDiscard
        {
            get => _confirmBeforeDiscard;
            set
            {
                if (Set(ref _confirmBeforeDiscard, value))
                    AppSettings.SaveConfirmBeforeDiscard(value);
            }
        }

        // ── Profile (default author identity) ───────────────────────────────────

        private string _defaultAuthorName;
        public string DefaultAuthorName
        {
            get => _defaultAuthorName;
            set { if (Set(ref _defaultAuthorName, value)) AppSettings.SaveDefaultIdentity(value, DefaultAuthorEmail); }
        }

        private string _defaultAuthorEmail;
        public string DefaultAuthorEmail
        {
            get => _defaultAuthorEmail;
            set { if (Set(ref _defaultAuthorEmail, value)) AppSettings.SaveDefaultIdentity(DefaultAuthorName, value); }
        }

        public ICommand SaveRepoIdentityCommand { get; }
        public ICommand ClearRepoIdentityCommand { get; }

        // ── Commands ──────────────────────────────────────────────────────────

        public ICommand OpenRepoCommand           { get; }
        public ICommand CloneRepoCommand          { get; }
        public ICommand InitRepoCommand           { get; }
        public ICommand CloseTabCommand           { get; }
        public ICommand ResetColumnWidthsCommand  { get; }
        public ICommand NextTabCommand            { get; }
        public ICommand PrevTabCommand            { get; }
        public ICommand CloseOtherTabsCommand     { get; }
        public ICommand CloseTabsToRightCommand   { get; }
        public ICommand CloseAllTabsCommand       { get; }
        public ICommand CopyRepoPathCommand       { get; }
        public ICommand OpenRepoInExplorerCommand { get; }
        public ICommand OpenTerminalCommand       { get; }
        public ICommand OpenSettingsCommand       { get; }
        public ICommand OpenHelpCommand           { get; }
        public ICommand OpenCommandPaletteCommand { get; }
        public ICommand FocusCommitSearchCommand  { get; }
        public ICommand FocusCommitListCommand    { get; }

        /// <summary>Raised by FocusCommitSearchCommand (Ctrl+F) — MainWindow opens and focuses
        /// the active tab's commit filter bar, which is a view concern.</summary>
        public event EventHandler CommitSearchRequested;

        /// <summary>Raised by FocusCommitListCommand (Ctrl+1) — MainWindow moves keyboard focus
        /// to the active tab's commit list for arrow-key navigation.</summary>
        public event EventHandler CommitListFocusRequested;

        public AppViewModel()
        {
            OpenRepoCommand          = new RelayCommand(OpenNewTab);
            CloneRepoCommand         = new RelayCommand(CloneInNewTab);
            InitRepoCommand          = new RelayCommand(InitInNewTab);
            CloseTabCommand          = new RelayCommand(CloseTab);
            ResetColumnWidthsCommand = new RelayCommand(ResetColumnWidths);
            NextTabCommand           = new RelayCommand(() => CycleTab(+1), () => Tabs.Count > 1);
            PrevTabCommand           = new RelayCommand(() => CycleTab(-1), () => Tabs.Count > 1);
            CloseOtherTabsCommand    = new RelayCommand(CloseOtherTabs, _ => Tabs.Count > 1);
            CloseTabsToRightCommand  = new RelayCommand(CloseTabsToRight,
                p => p is RepositoryViewModel t && Tabs.IndexOf(t) < Tabs.Count - 1);
            CloseAllTabsCommand      = new RelayCommand(CloseAllTabs, () => Tabs.Count > 0);
            CopyRepoPathCommand      = new RelayCommand(p =>
            {
                if (p is RepositoryViewModel t && t.RepoPath != null)
                    try { Clipboard.SetText(t.RepoPath); } catch { }
            });
            OpenRepoInExplorerCommand = new RelayCommand(p =>
            {
                if (p is RepositoryViewModel t && t.RepoPath != null)
                    try { System.Diagnostics.Process.Start("explorer.exe", $"\"{t.RepoPath}\""); } catch { }
            });
            OpenTerminalCommand = new RelayCommand(p =>
            {
                if (!(p is RepositoryViewModel t) || t.RepoPath == null) return;
                try
                {
                    // Prefer Windows Terminal; fall back to a plain cmd window
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo("wt.exe", $"-d \"{t.RepoPath}\"")
                        { UseShellExecute = true });
                }
                catch
                {
                    try
                    {
                        // Set the working directory via CreateProcess rather than embedding the
                        // path in the command-line string: cmd.exe's parser treats & | < > as
                        // control characters even inside double quotes, so a repo path containing
                        // one of those (legal in NTFS names) would inject an arbitrary command.
                        System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo("cmd.exe", "/K")
                            { UseShellExecute = true, WorkingDirectory = t.RepoPath });
                    }
                    catch { }
                }
            });
            OpenSettingsCommand = new RelayCommand(OpenSettings);
            OpenHelpCommand = new RelayCommand(OpenHelp);
            OpenCommandPaletteCommand = new RelayCommand(OpenCommandPalette);
            FocusCommitSearchCommand = new RelayCommand(
                () => CommitSearchRequested?.Invoke(this, EventArgs.Empty),
                () => ActiveTab != null);
            FocusCommitListCommand = new RelayCommand(
                () => CommitListFocusRequested?.Invoke(this, EventArgs.Empty),
                () => ActiveTab != null);
            SaveRepoIdentityCommand = new RelayCommand(
                () => AppSettings.SaveRepoIdentity(ActiveTab?.RepoPath, ActiveTab?.AuthorName, ActiveTab?.AuthorEmail),
                () => ActiveTab?.RepoPath != null);
            ClearRepoIdentityCommand = new RelayCommand(
                () => AppSettings.SaveRepoIdentity(ActiveTab?.RepoPath, null, null),
                () => ActiveTab?.RepoPath != null);

            LoadSettings();
        }

        private void CloseOtherTabs(object param)
        {
            if (!(param is RepositoryViewModel keep)) return;
            foreach (var tab in Tabs.Where(t => t != keep && !t.IsBusy).ToList())
            {
                Tabs.Remove(tab);
                tab.Dispose();
            }
            ActiveTab = keep;
            SaveSettings();
        }

        private void CloseTabsToRight(object param)
        {
            if (!(param is RepositoryViewModel anchor)) return;
            var idx = Tabs.IndexOf(anchor);
            if (idx < 0) return;
            foreach (var tab in Tabs.Skip(idx + 1).Where(t => !t.IsBusy).ToList())
            {
                Tabs.Remove(tab);
                tab.Dispose();
            }
            if (ActiveTab == null || !Tabs.Contains(ActiveTab)) ActiveTab = anchor;
            SaveSettings();
        }

        private void CloseAllTabs()
        {
            foreach (var tab in Tabs.Where(t => !t.IsBusy).ToList())
            {
                Tabs.Remove(tab);
                tab.Dispose();
            }
            ActiveTab = Tabs.FirstOrDefault();
            SaveSettings();
        }

        private void CycleTab(int direction)
        {
            if (Tabs.Count == 0) return;
            var idx = ActiveTab != null ? Tabs.IndexOf(ActiveTab) : 0;
            idx = (idx + direction + Tabs.Count) % Tabs.Count;
            ActiveTab = Tabs[idx];
        }

        private void OpenSettings()
        {
            var dlg = new Views.SettingsWindow(this) { Owner = Application.Current.MainWindow };
            dlg.ShowDialog();
        }

        private void OpenHelp()
        {
            var existing = Application.Current.Windows.OfType<Views.HelpWindow>().FirstOrDefault();
            if (existing != null) { existing.Activate(); return; }
            new Views.HelpWindow { Owner = Application.Current.MainWindow }.Show();
        }

        private void OpenCommandPalette()
        {
            var commands = AppCommandRegistry.Build(this);
            var dlg = new Views.CommandPaletteWindow(commands) { Owner = Application.Current.MainWindow };
            dlg.ShowDialog();
            var cmd = dlg.SelectedCommand;
            if (cmd?.Command != null && cmd.Command.CanExecute(cmd.CommandParameter))
                cmd.Command.Execute(cmd.CommandParameter);
        }

        private void ResetColumnWidths()
        {
            ColWidthBranchTag  = 120;
            ColWidthGraph      = 150;
            ColWidthCommitDesc = 200;
            ColWidthAuthor     = 140;
            ColWidthDateTime   = 110;
            ColWidthSha        = 80;
        }

        // ── Settings ──────────────────────────────────────────────────────────

        private void LoadSettings()
        {
            var (bt, g, cd, au, dt, sha) = AppSettings.LoadColumnSettings();
            _showBranchTag  = bt;
            _showGraph      = g;
            _showCommitDesc = cd;
            _showAuthor     = au;
            _showDateTime   = dt;
            _showSha        = sha;
            _smartBranchVisibility = AppSettings.LoadSmartBranchVisibility();
            _rerereEnabled = AppSettings.LoadRerereEnabled();
            _gpgSignCommits = AppSettings.LoadGpgSignCommits();
            _gitExePathOverride = AppSettings.LoadGitExePathOverride();
            _defaultCommitLoadLimit = AppSettings.LoadCommitLimit();
            _dateFormat = AppSettings.LoadDateFormat();
            Converters.DateFormatConverter.CurrentFormat = _dateFormat;
            _useRelativeDates = AppSettings.LoadUseRelativeDates();
            Converters.DateFormatConverter.UseRelative = _useRelativeDates;
            _confirmBeforeDiscard = AppSettings.LoadConfirmBeforeDiscard();
            (_defaultAuthorName, _defaultAuthorEmail) = AppSettings.LoadDefaultIdentity();

            var (wbt, wg, wcd, wau, wdt, wsha) = AppSettings.LoadColumnWidths();
            _colWidthBranchTag  = wbt;
            _colWidthGraph      = wg;
            _colWidthCommitDesc = wcd;
            _colWidthAuthor     = wau;
            _colWidthDateTime   = wdt;
            _colWidthSha        = wsha;

            var (sLocal, sRemote, sTags, sStashes, sRemotes, sReflog) = AppSettings.LoadSidebarSectionStates();
            _sidebarLocalBranchesExpanded  = sLocal;
            _sidebarRemoteBranchesExpanded = sRemote;
            _sidebarTagsExpanded           = sTags;
            _sidebarStashesExpanded        = sStashes;
            _sidebarRemotesExpanded        = sRemotes;
            _sidebarReflogExpanded         = sReflog;
        }

        public void SaveSettings()
        {
            AppSettings.SaveAppState(
                paths:        Tabs.Select(t => t.RepoPath).Where(p => p != null).ToList(),
                activePath:   _activeTab?.RepoPath,
                colBranchTag: _showBranchTag,
                colGraph:     _showGraph,
                colCommitDesc:_showCommitDesc,
                colAuthor:    _showAuthor,
                colDateTime:  _showDateTime,
                colSha:       _showSha,
                smartBranch:  _smartBranchVisibility,
                wBranchTag:   _colWidthBranchTag,
                wGraph:       _colWidthGraph,
                wCommitDesc:  _colWidthCommitDesc,
                wAuthor:      _colWidthAuthor,
                wDateTime:    _colWidthDateTime,
                wSha:         _colWidthSha);
        }

        // ── Tab management ────────────────────────────────────────────────────

        public async Task OpenRepoInNewTabAsync(string path, bool setActive = true, bool preloadSidebar = false)
        {
            var existing = Tabs.FirstOrDefault(t =>
                string.Equals(t.RepoPath, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { if (setActive) ActiveTab = existing; return; }

            var tab = new RepositoryViewModel();
            tab.SmartBranchVisibility = _smartBranchVisibility;
            tab.RequestOpenRepoInNewTab += p => _ = OpenRepoInNewTabAsync(p);
            Tabs.Add(tab);
            await tab.OpenRepoAsync(path);
            if (!tab.HasRepo)
            {
                Tabs.Remove(tab);
                tab.Dispose();
                return;
            }
            if (preloadSidebar)
                await tab.LoadSidebarAsync();
            if (setActive)
                ActiveTab = tab;  // triggers EnsureLoadedAsync via setter
            SaveSettings();
        }

        private void OpenNewTab()
        {
            var path = Services.ShellFolderPicker.ShowDialog("Select a Git repository folder");
            if (path != null)
                _ = OpenRepoInNewTabAsync(path);
        }

        private void CloneInNewTab()
        {
            var dlg = new Views.CloneDialog { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() == true)
                _ = CloneInNewTabAsync(dlg.RemoteUrl, dlg.LocalPath, dlg.Username, dlg.Password,
                    dlg.Branch, dlg.RecurseSubmodules);
        }

        private async Task CloneInNewTabAsync(string url, string localPath,
            string username, string password, string branch = null, bool recurseSubmodules = false)
        {
            var tab = new RepositoryViewModel();
            tab.SmartBranchVisibility = _smartBranchVisibility;
            tab.RequestOpenRepoInNewTab += p => _ = OpenRepoInNewTabAsync(p);
            Tabs.Add(tab);
            ActiveTab = tab;
            await tab.CloneRepoAsync(url, localPath, username, password, branch, recurseSubmodules);
            if (!tab.HasRepo)
            {
                Tabs.Remove(tab);
                tab.Dispose();
            }
            else
            {
                try
                {
                    var parent = System.IO.Path.GetDirectoryName(localPath.TrimEnd('\\', '/'));
                    if (!string.IsNullOrEmpty(parent)) AppSettings.SaveLastCloneParentDir(parent);
                }
                catch { }
                SaveSettings();
            }
        }

        private async void InitInNewTab()
        {
            var path = Services.ShellFolderPicker.ShowDialog("Select folder to initialize as a Git repository");
            if (path == null) return;
            // No RepositoryViewModel/GitService.Executor exists yet for this path — Repository.Init
            // doesn't touch any existing (potentially shared) Repository handle, so a plain Task.Run
            // is safe here specifically, unlike every other LibGit2Sharp call in the app, which must
            // go through an already-open repo's dedicated executor thread.
            await Task.Run(() => GitService.Init(path));
            _ = OpenRepoInNewTabAsync(path);
        }

        private void CloseTab(object param)
        {
            if (!(param is RepositoryViewModel tab)) return;
            if (tab.IsBusy) return;
            var idx = Tabs.IndexOf(tab);
            Tabs.Remove(tab);
            tab.Dispose();
            if (ActiveTab == tab)
                ActiveTab = Tabs.Count > 0 ? Tabs[Math.Max(0, idx - 1)] : null;
            SaveSettings();
        }
    }
}
