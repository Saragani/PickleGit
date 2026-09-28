using System.Windows;
using PickleGit.Services;

namespace PickleGit.Views.Dialogs
{
    public partial class ErrorDialog : Window
    {
        public string DialogTitle { get; set; } = "Error";
        public string HeaderText { get; set; } = "Something went wrong";
        public string MessageText { get; set; } = string.Empty;
        public string DetailsText { get; set; }

        public Visibility DetailsVisibility =>
            string.IsNullOrWhiteSpace(DetailsText) ? Visibility.Collapsed : Visibility.Visible;

        public ErrorDialog()
        {
            InitializeComponent();
            DataContext = this;
            // Cap growth to the actual monitor's usable work area so a long MessageText scrolls
            // internally instead of pushing the Copy/OK row past the window's own bounds.
            // Deferred to SourceInitialized since Owner is only assigned (by DialogService's
            // object initializer) after this constructor returns.
            SourceInitialized += (s, e) => MaxHeight = DialogSizing.ForOwner(Owner);
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(string.IsNullOrWhiteSpace(DetailsText)
                    ? MessageText
                    : MessageText + "\n\n" + DetailsText);
            }
            catch { }
        }
    }
}
