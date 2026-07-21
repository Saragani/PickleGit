using System.Windows;

namespace PickleGit.Views.Dialogs
{
    public partial class TextPromptDialog : Window
    {
        public string DialogTitle { get; set; } = "PickleGit";
        public string HeaderText { get; set; } = string.Empty;
        public string PromptText { get; set; } = string.Empty;
        public string InputText { get; set; } = string.Empty;
        public string OkText { get; set; } = "OK";

        /// <summary>When false, an empty input is allowed (e.g. optional stash message).</summary>
        public bool RequireInput { get; set; } = true;

        /// <summary>Optional checkbox below the input (hidden when text is null/empty).</summary>
        public string CheckboxText { get; set; }
        public bool IsCheckboxChecked { get; set; }
        public Visibility CheckboxVisibility =>
            string.IsNullOrEmpty(CheckboxText) ? Visibility.Collapsed : Visibility.Visible;

        public TextPromptDialog()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += (s, e) =>
            {
                InputBox.Focus();
                InputBox.SelectAll();
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (RequireInput && string.IsNullOrWhiteSpace(InputText))
            {
                InputBox.Focus();
                return;
            }
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
