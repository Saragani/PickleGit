using System.Windows;

namespace PickleGit.Views
{
    public partial class CredentialsDialog : Window
    {
        public string Username
        {
            get => UsernameBox.Text;
            set => UsernameBox.Text = value ?? string.Empty;
        }

        public string Password { get; private set; } = string.Empty;

        public CredentialsDialog()
        {
            InitializeComponent();
        }

        private void PassBox_PasswordChanged(object sender, RoutedEventArgs e)
            => Password = PassBox.Password;

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                ValidationText.Text = "Please enter a username.";
                ValidationText.Visibility = Visibility.Visible;
                UsernameBox.Focus();
                return;
            }
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
