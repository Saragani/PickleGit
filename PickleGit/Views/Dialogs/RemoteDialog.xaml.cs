using System.Windows;

namespace PickleGit.Views.Dialogs
{
    public partial class RemoteDialog : Window
    {
        public string DialogTitle { get; set; } = "Remote";
        public string HeaderText { get; set; } = "Remote";
        public string OkText { get; set; } = "Save";
        public string RemoteName { get; set; } = string.Empty;
        public string RemoteUrl { get; set; } = string.Empty;

        /// <summary>False when editing an existing remote (git remotes are renamed separately).</summary>
        public bool IsNameEditable { get; set; } = true;

        public RemoteDialog()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += (s, e) =>
            {
                if (IsNameEditable) NameBox.Focus(); else UrlBox.Focus();
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(RemoteName)) { NameBox.Focus(); return; }
            if (string.IsNullOrWhiteSpace(RemoteUrl)) { UrlBox.Focus(); return; }
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
