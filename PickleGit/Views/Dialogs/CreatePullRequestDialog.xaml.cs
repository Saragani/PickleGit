using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PickleGit.Views.Dialogs
{
    public partial class CreatePullRequestDialog : Window, INotifyPropertyChanged
    {
        public string DialogTitle { get; set; } = "Create Pull Request";
        public string HeaderText { get; set; } = "Create Pull Request";
        public string OkText { get; set; } = "Create";

        public string SourceBranch { get; set; } = string.Empty;
        public List<string> TargetBranches { get; set; } = new List<string>();
        public string SelectedTargetBranch { get; set; } = string.Empty;

        public string PrTitle { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsDraft { get; set; }
        public bool SupportsDraft { get; set; } = true;

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasStatusText))); }
        }
        public bool HasStatusText => !string.IsNullOrEmpty(StatusText);

        public event PropertyChangedEventHandler PropertyChanged;

        public CreatePullRequestDialog()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += (s, e) =>
            {
                TargetBox.ItemsSource = TargetBranches;
                TitleBox.Focus();
            };
        }

        // The "Into" combo is editable so the user can type to filter — WPF has no built-in
        // type-to-filter for ComboBox, so IsTextSearchEnabled is off and the item list is
        // re-filtered by hand here instead of relying on the (single-char, jump-to-match) default.
        private void TargetBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = TargetBox.Text ?? string.Empty;
            var filtered = string.IsNullOrEmpty(text)
                ? TargetBranches
                : TargetBranches.Where(t => t.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            TargetBox.ItemsSource = filtered;

            // Clicking a suggestion sets both Text and SelectedItem to the same string and closes
            // the popup itself — don't force it back open right after that commit.
            var justCommitted = TargetBox.SelectedItem is string sel && string.Equals(sel, text, StringComparison.Ordinal);
            if (filtered.Count > 0 && !justCommitted && TargetBox.IsFocused) TargetBox.IsDropDownOpen = true;
        }

        private void TargetBox_GotFocus(object sender, RoutedEventArgs e)
        {
            TargetBox.IsDropDownOpen = true;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PrTitle)) { StatusText = "Enter a title for the pull request."; TitleBox.Focus(); return; }
            if (string.IsNullOrWhiteSpace(SelectedTargetBranch)) { StatusText = "Choose a target branch."; TargetBox.Focus(); return; }
            var match = TargetBranches.FirstOrDefault(t => string.Equals(t, SelectedTargetBranch, StringComparison.Ordinal));
            if (match == null)
            {
                StatusText = $"'{SelectedTargetBranch}' isn't one of the available remote branches.";
                TargetBox.Focus();
                return;
            }
            SelectedTargetBranch = match;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
