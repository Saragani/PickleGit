using System.Windows;
using PickleGit.ViewModels;

namespace PickleGit.Views
{
    public partial class InteractiveRebaseDialog : Window
    {
        public InteractiveRebaseDialog()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                if (e.OldValue is InteractiveRebaseViewModel oldVm) oldVm.RequestClose -= OnRequestClose;
                if (e.NewValue is InteractiveRebaseViewModel newVm) newVm.RequestClose += OnRequestClose;
            };
        }

        private void OnRequestClose(bool started) => DialogResult = started;
    }
}
