using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace PickleGit.Views
{
    public partial class CommitDetailView : UserControl
    {
        public CommitDetailView()
        {
            InitializeComponent();
        }

        private void CommitOptionsButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (!(sender is Button btn) || btn.ContextMenu == null) return;
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = PlacementMode.Bottom;
            // ContextMenu is not in the visual tree — give it this view's DataContext.
            btn.ContextMenu.DataContext = DataContext;
            btn.ContextMenu.IsOpen = true;
        }
    }
}
