using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PickleGit.Services;
using PickleGit.Services.Hosting;
using PickleGit.ViewModels;

namespace PickleGit.Views
{
    public partial class SettingsWindow : Window
    {
        public class HostTokenEntry
        {
            public string Domain { get; set; }
            public string Username { get; set; }
        }

        public class ShortcutEntry
        {
            public string Id { get; set; }
            public string DisplayName { get; set; }
            public string GestureText { get; set; }
        }

        private Dictionary<string, ScrollViewer> _sections;
        private Dictionary<string, Button> _sectionButtons;

        public SettingsWindow(AppViewModel appViewModel, string initialSection = null)
        {
            InitializeComponent();
            DataContext = appViewModel;
            _sections = new Dictionary<string, ScrollViewer>
            {
                ["General"] = GeneralSection,
                ["Profile"] = ProfileSection,
                ["UI"] = UISection,
                ["GitBehavior"] = GitBehaviorSection,
                ["Integrations"] = IntegrationsSection,
                ["Shortcuts"] = ShortcutsSection,
                ["About"] = AboutSection,
            };
            _sectionButtons = new Dictionary<string, Button>
            {
                ["General"] = GeneralSectionButton,
                ["Profile"] = ProfileSectionButton,
                ["UI"] = UISectionButton,
                ["GitBehavior"] = GitBehaviorSectionButton,
                ["Integrations"] = IntegrationsSectionButton,
                ["Shortcuts"] = ShortcutsSectionButton,
                ["About"] = AboutSectionButton,
            };
            RefreshConfiguredHosts();
            RefreshShortcuts();
            RefreshSelfHosted();
            InitializeTheme();
            PopulateAbout();
            ShowSection(initialSection ?? "General");
        }

        private bool _themeInitialized;

        private void InitializeTheme()
        {
            ThemeBox.SelectedIndex = AppSettings.LoadTheme() == "Light" ? 1 : 0;
            _themeInitialized = true;
        }

        private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_themeInitialized) return;
            var kind = (ThemeBox.SelectedItem as ComboBoxItem)?.Tag as string;
            if (kind == null) return;
            AppSettings.SaveTheme(kind);
            App.ApplyTheme(kind);
        }

        private void PopulateAbout()
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var version = asm.GetName().Version;
            VersionText.Text = $"Version {version} · .NET Framework 4.7.2 · x64";
            var appData = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "PickleGit");
            SettingsPathText.Text = System.IO.Path.Combine(appData, "settings.json");
            LogPathText.Text = System.IO.Path.Combine(appData, "logs", "picklegit.log");
        }

        private void ShowSection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string key)
                ShowSection(key);
        }

        private void ShowSection(string key)
        {
            if (!_sections.TryGetValue(key, out var target)) return;
            foreach (var section in _sections.Values)
                section.Visibility = ReferenceEquals(section, target) ? Visibility.Visible : Visibility.Collapsed;

            foreach (var entry in _sectionButtons)
            {
                var isActive = entry.Key == key;
                entry.Value.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
                if (isActive)
                    entry.Value.SetResourceReference(Button.BackgroundProperty, "SurfaceAltBrush");
                else
                    entry.Value.ClearValue(Button.BackgroundProperty);
            }
        }

        private void RefreshConfiguredHosts()
        {
            ConfiguredHostsList.ItemsSource = HostingCredentials.ListConfigured()
                .Select(t => new HostTokenEntry { Domain = t.domain, Username = t.username })
                .OrderBy(e => e.Domain)
                .ToList();
        }

        private void RefreshShortcuts()
        {
            ShortcutsList.ItemsSource = ShortcutManager.Actions
                .Select(a => new ShortcutEntry { Id = a.Id, DisplayName = a.DisplayName, GestureText = ShortcutManager.GetGesture(a.Id) })
                .ToList();
        }

        private void SaveShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.Tag is ShortcutEntry entry)) return;
            var gesture = entry.GestureText?.Trim();
            if (!string.IsNullOrEmpty(gesture))
            {
                try { new KeyGestureConverter().ConvertFromString(gesture); }
                catch
                {
                    DialogService.ShowError("Invalid Shortcut",
                        $"'{gesture}' isn't a recognized key combination (e.g. Ctrl+Shift+K).");
                    return;
                }
                var conflict = ShortcutManager.FindConflict(gesture, entry.Id);
                if (conflict != null)
                {
                    DialogService.ShowError("Shortcut Already In Use",
                        $"'{gesture}' is already assigned to \"{conflict}\". Choose a different combination or reset that shortcut first.");
                    return;
                }
            }
            ShortcutManager.SetGesture(entry.Id, gesture);
            (Owner as MainWindow)?.RebuildInputBindings();
            RefreshShortcuts();
        }

        private void ResetShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.Tag is ShortcutEntry entry)) return;
            ShortcutManager.ResetGesture(entry.Id);
            (Owner as MainWindow)?.RebuildInputBindings();
            RefreshShortcuts();
        }

        private void BrowseGitPath_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select git.exe",
                Filter = "git.exe|git.exe|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) == true)
                GitPathBox.Text = dlg.FileName;
        }

        public class SelfHostedEntry
        {
            public string Domain { get; set; }
            public string Kind { get; set; }
            public string KindLabel => Kind == "github" ? "GitHub Enterprise" : "GitLab (self-managed)";
        }

        private void RefreshSelfHosted()
        {
            SelfHostedList.ItemsSource = AppSettings.LoadHostingDomainKinds()
                .Select(kv => new SelfHostedEntry { Domain = kv.Key, Kind = kv.Value })
                .OrderBy(x => x.Domain)
                .ToList();
        }

        private void AddSelfHosted_Click(object sender, RoutedEventArgs e)
        {
            var domain = SelfHostedDomainBox.Text?.Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(domain)) return;
            // Accept a pasted URL too — keep just the host
            if (domain.Contains("://") && System.Uri.TryCreate(domain, System.UriKind.Absolute, out var uri))
                domain = uri.Host;
            var kind = (SelfHostedKindBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "github";
            AppSettings.SaveHostingDomainKind(domain, kind);
            SelfHostedDomainBox.Text = string.Empty;
            RefreshSelfHosted();
        }

        private void RemoveSelfHosted_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.Tag is SelfHostedEntry entry)) return;
            AppSettings.SaveHostingDomainKind(entry.Domain, null);
            RefreshSelfHosted();
        }

        private void SaveHostToken_Click(object sender, RoutedEventArgs e)
        {
            var domain = HostDomainBox.Text?.Trim();
            var username = HostUsernameBox.Text?.Trim();
            var secret = HostSecretBox.Password;
            if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(secret)) return;

            HostingCredentials.Save(domain, username, secret);
            HostSecretBox.Password = string.Empty;
            RefreshConfiguredHosts();
        }

        private void RemoveHostToken_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.Tag is HostTokenEntry entry)) return;
            HostingCredentials.Delete(entry.Domain, entry.Username);
            RefreshConfiguredHosts();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
