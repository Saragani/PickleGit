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

        /// <summary>Prompts for a 1-based line number via the standard themed prompt dialog.
        /// Shared by every "Go to Line" (Ctrl+G) surface — the diff view, blame view, and the
        /// merge-conflict window's panes. Returns null when cancelled or the input isn't a valid
        /// positive integer.</summary>
        public static int? PromptForLineNumber(string title = "Go to Line")
        {
            var text = Prompt(title, "Line number:", okText: "Go");
            if (text == null) return null;
            return int.TryParse(text.Trim(), out int line) && line > 0 ? line : (int?)null;
        }

        public static bool Confirm(string title, string message,
            string okText = "OK", bool danger = false, string cancelText = "Cancel")
        {
            var dlg = new ConfirmDialog
            {
                Owner = GetOwner(),
                DialogTitle = title,
                HeaderText = title,
                MessageText = message,
                OkText = okText,
                CancelText = cancelText,
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
