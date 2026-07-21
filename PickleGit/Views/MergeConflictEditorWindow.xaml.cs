using System.Windows;
using PickleGit.ViewModels;

namespace PickleGit.Views
{
    public partial class MergeConflictEditorWindow : Window
    {
        public MergeConflictEditorWindow()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                if (e.OldValue is MergeConflictEditorViewModel oldVm) oldVm.RequestClose -= OnRequestClose;
                if (e.NewValue is MergeConflictEditorViewModel newVm) newVm.RequestClose += OnRequestClose;
            };
        }

        private void OnRequestClose(bool saved) => DialogResult = saved;
    }
}
