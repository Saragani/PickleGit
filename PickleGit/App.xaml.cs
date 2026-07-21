using System.Windows;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using PickleGit.Services;
using PickleGit.ViewModels;
using PickleGit.Views;

namespace PickleGit
{
    public partial class App : Application
    {
        private const string MutexName = "PickleGit-SingleInstance";
        private const string PipeName = "PickleGit-ActivationPipe";
        private System.Threading.Mutex _instanceMutex;
        private AppViewModel _viewModel;

        /// <summary>True when the light palette is active — MainWindow skips the dark title bar.</summary>
        public static bool IsLightTheme { get; private set; }

        /// <summary>Swaps the merged palette dictionary at runtime so a theme change in Settings
        /// applies immediately — every style in DarkTheme.xaml references palette keys via
        /// DynamicResource, which re-resolves live when its source dictionary changes, so no
        /// restart is needed. Also re-applies the DWM dark-title-bar attribute (a native window
        /// attribute, not a DynamicResource) to every currently open window.</summary>
        public static void ApplyTheme(string theme)
        {
            var app = Current;
            if (app == null) return;

            IsLightTheme = theme == "Light";

            var dictionaries = app.Resources.MergedDictionaries;
            for (int i = dictionaries.Count - 1; i >= 0; i--)
            {
                var source = dictionaries[i].Source?.OriginalString;
                if (source != null && source.StartsWith("Themes/Palette", StringComparison.OrdinalIgnoreCase))
                    dictionaries.RemoveAt(i);
            }
            dictionaries.Insert(0, new ResourceDictionary
            {
                Source = new Uri($"Themes/Palette{theme}.xaml", UriKind.Relative)
            });

            foreach (Window window in app.Windows)
                Services.TitleBarTheme.Apply(window, !IsLightTheme);
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ── Theme: merge the chosen palette FIRST, before any window is created.
            //    Styles (Themes/DarkTheme.xaml, merged in App.xaml) reference palette keys
            //    via DynamicResource, so they pick up whichever palette lands here.
            var theme = AppSettings.LoadTheme();
            IsLightTheme = theme == "Light";
            Resources.MergedDictionaries.Insert(0, new ResourceDictionary
            {
                Source = new Uri($"Themes/Palette{theme}.xaml", UriKind.Relative)
            });

            // --test-instance: run fully isolated (no single-instance handshake) for UI
            // automation/testing. Combine with PICKLEGIT_APPDATA to isolate settings too.
            var isTestInstance = Environment.GetCommandLineArgs()
                .Any(a => string.Equals(a, "--test-instance", StringComparison.OrdinalIgnoreCase));

            // ── Single instance: a second launch forwards its repo path to the running
            //    instance over a named pipe (opens a tab there) and exits. Two instances
            //    would otherwise race on settings.json.
            if (!isTestInstance)
            {
                _instanceMutex = new System.Threading.Mutex(true, MutexName, out bool isFirstInstance);
                if (!isFirstInstance)
                {
                    var argsEarly = Environment.GetCommandLineArgs();
                    ForwardToRunningInstance(argsEarly.Length > 1 ? argsEarly[1] : string.Empty);
                    Shutdown();
                    return;
                }
                StartActivationPipeServer();
            }
            // Handle unobserved task exceptions gracefully (logged so failures stay diagnosable)
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, ex) =>
            {
                AppLog.Error("Unobserved task exception", ex.Exception);
                ex.SetObserved();
            };
            // A single bad binding or command handler should never take down the whole app.
            DispatcherUnhandledException += (s, ex) =>
            {
                AppLog.Error("Dispatcher unhandled exception", ex.Exception);
                Services.DialogService.ShowError("Unexpected Error", ex.Exception.Message, ex.Exception.ToString());
                ex.Handled = true;
            };

            Services.Git.GitCli.GitPathOverride = AppSettings.LoadGitExePathOverride();

            var splashModel = new SplashStatusViewModel { StatusMessage = "Starting..." };
            var splash = new SplashWindow { DataContext = splashModel };
            splash.Show();

            var viewModel = new AppViewModel();
            _viewModel = viewModel;
            RepositoryViewModel activeTab = null;

            try
            {
                var savedPaths = AppSettings.LoadOpenRepos();
                var activePath = AppSettings.LoadActiveRepo();
                var args = Environment.GetCommandLineArgs();
                string cmdLinePath = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--"));

                var allPaths = savedPaths.ToList();
                if (cmdLinePath != null && !allPaths.Any(p =>
                        string.Equals(p, cmdLinePath, StringComparison.OrdinalIgnoreCase)))
                    allPaths.Add(cmdLinePath);

                for (int i = 0; i < allPaths.Count; i++)
                {
                    var path = allPaths[i];
                    splashModel.StatusMessage = $"Loading repository {i + 1} of {allPaths.Count}";
                    await viewModel.OpenRepoInNewTabAsync(path, setActive: false, preloadSidebar: true);
                }

                activeTab = activePath != null
                    ? viewModel.Tabs.FirstOrDefault(t =>
                        string.Equals(t.RepoPath, activePath, StringComparison.OrdinalIgnoreCase))
                    : null;
                if (activeTab == null && viewModel.Tabs.Count > 0)
                    activeTab = viewModel.Tabs[0];
            }
            finally
            {
                var main = new MainWindow(viewModel);
                MainWindow = main;
                main.Show();
                splash.Close();
            }

            if (activeTab != null)
                viewModel.ActiveTab = activeTab;
        }

        private static void ForwardToRunningInstance(string repoPath)
        {
            try
            {
                using (var client = new System.IO.Pipes.NamedPipeClientStream(".", PipeName,
                    System.IO.Pipes.PipeDirection.Out))
                {
                    client.Connect(2000);
                    var bytes = System.Text.Encoding.UTF8.GetBytes(repoPath ?? string.Empty);
                    client.Write(bytes, 0, bytes.Length);
                }
            }
            catch (Exception ex) { AppLog.Warn("Failed to forward launch to running instance", ex); }
        }

        /// <summary>Background pipe server: each message is a repo path (or empty = just activate).</summary>
        private void StartActivationPipeServer()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        using (var server = new System.IO.Pipes.NamedPipeServerStream(PipeName,
                            System.IO.Pipes.PipeDirection.In, 1,
                            System.IO.Pipes.PipeTransmissionMode.Byte,
                            System.IO.Pipes.PipeOptions.Asynchronous))
                        {
                            await server.WaitForConnectionAsync();
                            var buffer = new byte[4096];
                            int read = await server.ReadAsync(buffer, 0, buffer.Length);
                            var path = System.Text.Encoding.UTF8.GetString(buffer, 0, read).Trim();
                            _ = Dispatcher.BeginInvoke(new Action(async () =>
                            {
                                var main = MainWindow;
                                if (main != null)
                                {
                                    if (main.WindowState == WindowState.Minimized)
                                        main.WindowState = WindowState.Normal;
                                    main.Activate();
                                }
                                if (!string.IsNullOrEmpty(path) && _viewModel != null)
                                {
                                    var existing = _viewModel.Tabs.FirstOrDefault(t =>
                                        string.Equals(t.RepoPath, path, StringComparison.OrdinalIgnoreCase));
                                    if (existing != null) _viewModel.ActiveTab = existing;
                                    else await _viewModel.OpenRepoInNewTabAsync(path, setActive: true);
                                }
                            }));
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("Activation pipe server error", ex);
                        await Task.Delay(1000);
                    }
                }
            });
        }

        private sealed class SplashStatusViewModel : INotifyPropertyChanged
        {
            private string _statusMessage;
            public string StatusMessage
            {
                get => _statusMessage;
                set
                {
                    if (_statusMessage == value) return;
                    _statusMessage = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }
    }
}
