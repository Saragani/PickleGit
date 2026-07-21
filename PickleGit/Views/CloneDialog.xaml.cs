using System.Windows;

namespace PickleGit.Views
{
    public partial class CloneDialog : Window
    {
        public string RemoteUrl { get; set; } = string.Empty;
        public string LocalPath { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public bool RecurseSubmodules { get; set; }
        public string Password { get; private set; } = string.Empty;

        public CloneDialog()
        {
            InitializeComponent();
            DataContext = this;
            // Derive the destination from the remembered parent folder as the URL is typed
            UrlBox.TextChanged += (s, e) => AutoFillLocalPath();
        }

        private void AutoFillLocalPath()
        {
            var parent = Services.AppSettings.LoadLastCloneParentDir();
            if (string.IsNullOrEmpty(parent) || !System.IO.Directory.Exists(parent)) return;
            // Only auto-fill while the user hasn't typed a path of their own
            if (!string.IsNullOrEmpty(LocalPath) && !LocalPath.StartsWith(parent, System.StringComparison.OrdinalIgnoreCase))
                return;
            var repoName = RepoNameFromUrl(RemoteUrl);
            if (repoName == null) return;
            LocalPath = System.IO.Path.Combine(parent, repoName);
            PathBox.Text = LocalPath;
        }

        private static string RepoNameFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var trimmed = url.TrimEnd('/');
            var name = System.IO.Path.GetFileNameWithoutExtension(trimmed);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var path = Services.ShellFolderPicker.ShowDialog("Select destination folder");
            if (path != null)
            {
                Services.AppSettings.SaveLastCloneParentDir(path);
                LocalPath = System.IO.Path.Combine(path,
                    System.IO.Path.GetFileNameWithoutExtension(RemoteUrl?.TrimEnd('/')));
                PathBox.Text = LocalPath;
            }
        }

        private void PassBox_PasswordChanged(object sender, RoutedEventArgs e)
            => Password = PassBox.Password;

        private void Clone_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(RemoteUrl))
            {
                ShowValidation("Please enter a repository URL.");
                UrlBox.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(LocalPath))
            {
                ShowValidation("Please select a local directory.");
                PathBox.Focus();
                return;
            }
            DialogResult = true;
        }

        private void ShowValidation(string message)
        {
            ValidationText.Text = message;
            ValidationText.Visibility = Visibility.Visible;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
