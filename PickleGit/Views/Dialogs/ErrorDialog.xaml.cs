using System.Windows;

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
