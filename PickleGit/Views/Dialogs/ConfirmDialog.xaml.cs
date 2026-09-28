using System.Windows;
using System.Windows.Controls;
using PickleGit.Services;

namespace PickleGit.Views.Dialogs
{
    public partial class ConfirmDialog : Window
    {
        public string DialogTitle { get; set; } = "PickleGit";
        public string HeaderText { get; set; } = string.Empty;
        public string MessageText { get; set; } = string.Empty;
        public string OkText { get; set; } = "OK";
        public string CancelText { get; set; } = "Cancel";

        /// <summary>Styles the confirm button red for destructive actions.</summary>
        public bool IsDanger { get; set; }

        public ConfirmDialog()
        {
            InitializeComponent();
            DataContext = this;
            // Cap growth to the actual monitor's usable work area so a long MessageText scrolls
            // internally instead of pushing the OK/Cancel row off-screen. Deferred to
            // SourceInitialized since Owner is only assigned (by DialogService's object
            // initializer) after this constructor returns.
            SourceInitialized += (s, e) => MaxHeight = DialogSizing.ForOwner(Owner);
            Loaded += (s, e) =>
            {
                var styleKey = IsDanger ? "DangerButton" : "AccentButton";
                if (TryFindResource(styleKey) is Style style)
                    OkButton.Style = style;
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
