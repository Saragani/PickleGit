using System.Windows;

namespace PickleGit.Views
{
    public partial class NewBranchDialog : Window
    {
        public string BranchName { get; set; } = string.Empty;

        public NewBranchDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(BranchName))
            {
                ValidationText.Text = "Please enter a branch name.";
                ValidationText.Visibility = Visibility.Visible;
                NameBox.Focus();
                return;
            }
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
