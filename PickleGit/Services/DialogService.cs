using System.Windows;
using PickleGit.Views.Dialogs;

namespace PickleGit.Services
{
    /// <summary>
    /// Themed replacements for MessageBox / VB InputBox. Must be called on the
    /// UI thread. Owner defaults to the app main window when it is visible.
    /// </summary>
    public static class DialogService
    {
        private static Window GetOwner()
        {
            var main = Application.Current?.MainWindow;
            return (main != null && main.IsVisible) ? main : null;
        }

        /// <summary>Returns the entered text, or null when cancelled.</summary>
        public static string Prompt(string title, string prompt, string initial = "",
            string okText = "OK", bool requireInput = true)
        {
            var dlg = new TextPromptDialog
            {
                Owner = GetOwner(),
                DialogTitle = title,
                HeaderText = title,
                PromptText = prompt,
                InputText = initial ?? string.Empty,
                OkText = okText,
                RequireInput = requireInput
            };
            return dlg.ShowDialog() == true ? dlg.InputText : null;
        }

        public static bool Confirm(string title, string message,
            string okText = "OK", bool danger = false)
        {
            var dlg = new ConfirmDialog
            {
                Owner = GetOwner(),
                DialogTitle = title,
                HeaderText = title,
                MessageText = message,
                OkText = okText,
                IsDanger = danger
            };
            return dlg.ShowDialog() == true;
        }

        public static void ShowError(string title, string message, string details = null)
        {
            var dlg = new ErrorDialog
            {
                Owner = GetOwner(),
                DialogTitle = title,
                HeaderText = title,
                MessageText = message,
                DetailsText = details
            };
            dlg.ShowDialog();
        }
    }
}
