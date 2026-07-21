using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PickleGit.Models
{
    /// <summary>Edit = stop at this commit to amend it; Break = stop *after* applying it
    /// (todo gets a `break` line). Both resume via the conflict banner's Continue button.</summary>
    public enum RebaseTodoAction { Pick, Reword, Squash, Fixup, Edit, Break, Drop }

    /// <summary>One row of the interactive rebase todo list (Views/InteractiveRebaseDialog.xaml).</summary>
    public class RebaseTodoItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public string Sha { get; set; }
        public string ShortSha => string.IsNullOrEmpty(Sha) ? string.Empty : Sha.Substring(0, System.Math.Min(7, Sha.Length));

        public string Message { get; set; }
        public string MessageShort => string.IsNullOrEmpty(Message) ? string.Empty : Message.Split('\n')[0].Trim();

        private RebaseTodoAction _action = RebaseTodoAction.Pick;
        public RebaseTodoAction Action
        {
            get => _action;
            set { if (_action == value) return; _action = value; Raise(); Raise(nameof(IsReword)); }
        }

        public bool IsReword => Action == RebaseTodoAction.Reword;

        /// <summary>Replacement commit message, used only when Action == Reword.</summary>
        private string _newMessage;
        public string NewMessage { get => _newMessage; set { _newMessage = value; Raise(); } }
    }
}
