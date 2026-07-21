using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using PickleGit.Services;

namespace PickleGit.Views
{
    public partial class CommandPaletteWindow : Window
    {
        private readonly List<PaletteCommand> _all;
        private bool _closing;

        /// <summary>The command the user picked, or null if dismissed. Read by the caller
        /// after ShowDialog() returns — executing here (while this window is still mid-close)
        /// would throw if the command opens another window.</summary>
        public PaletteCommand SelectedCommand { get; private set; }

        public CommandPaletteWindow(List<PaletteCommand> commands)
        {
            InitializeComponent();
            _all = commands;
            ResultsList.ItemsSource = _all;
            Loaded += (s, e) =>
            {
                SearchBox.Focus();
                if (ResultsList.Items.Count > 0) ResultsList.SelectedIndex = 0;
            };
        }

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var filtered = AppCommandRegistry.Filter(_all, SearchBox.Text).ToList();
            ResultsList.ItemsSource = filtered;
            if (filtered.Count > 0) ResultsList.SelectedIndex = 0;
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    if (ResultsList.SelectedIndex < ResultsList.Items.Count - 1) ResultsList.SelectedIndex++;
                    ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                    e.Handled = true;
                    break;
                case Key.Up:
                    if (ResultsList.SelectedIndex > 0) ResultsList.SelectedIndex--;
                    ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    Choose(ResultsList.SelectedItem as PaletteCommand);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    CloseOnce();
                    e.Handled = true;
                    break;
            }
        }

        private void ResultsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => Choose(ResultsList.SelectedItem as PaletteCommand);

        private void Choose(PaletteCommand cmd)
        {
            SelectedCommand = cmd;
            CloseOnce();
        }

        // Closing (e.g. to let a chosen command open its own dialog) shifts focus away from this
        // window, which re-triggers Deactivated — guard so we don't call Close() while it's already closing.
        private void CloseOnce()
        {
            if (_closing) return;
            _closing = true;
            Close();
        }

        private void Window_Deactivated(object sender, System.EventArgs e) => CloseOnce();
    }
}
