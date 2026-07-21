using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using PickleGit.Models;
using PickleGit.Services;

namespace PickleGit.ViewModels
{
    /// <summary>
    /// Backs Views/InteractiveRebaseDialog.xaml — lets the user pick/reword/squash/fixup/drop
    /// and reorder the commits between the rebase target and HEAD. Purely a todo-list editor:
    /// it does not touch git itself. The caller (RepositoryViewModel.StartInteractiveRebaseAsync)
    /// reads <see cref="Items"/> after a successful Start and drives the actual `git rebase -i`.
    /// </summary>
    public class InteractiveRebaseViewModel : BaseViewModel
    {
        private readonly GitService _git;
        private readonly string _ontoSha;

        public string OntoLabel { get; }
        public ObservableCollection<RebaseTodoItem> Items { get; } = new ObservableCollection<RebaseTodoItem>();

        private bool _isLoading = true;
        public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }

        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }
        public ICommand CycleActionCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand CancelCommand { get; }

        /// <summary>True = user confirmed the plan and it should be run; false/null = cancelled.</summary>
        public event Action<bool> RequestClose;

        public InteractiveRebaseViewModel(GitService git, string ontoSha, string ontoLabel)
        {
            _git = git;
            _ontoSha = ontoSha;
            OntoLabel = ontoLabel;

            MoveUpCommand = new RelayCommand(p => Move(p as RebaseTodoItem, -1));
            MoveDownCommand = new RelayCommand(p => Move(p as RebaseTodoItem, 1));
            CycleActionCommand = new RelayCommand(p => CycleAction(p as RebaseTodoItem));
            StartCommand = new RelayCommand(Start, () => !IsLoading && Items.Count > 0);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));

            _ = LoadAsync();
        }

        private static readonly RebaseTodoAction[] ActionCycle = (RebaseTodoAction[])Enum.GetValues(typeof(RebaseTodoAction));

        private static void CycleAction(RebaseTodoItem item)
        {
            if (item == null) return;
            var idx = Array.IndexOf(ActionCycle, item.Action);
            item.Action = ActionCycle[(idx + 1) % ActionCycle.Length];
        }

        private async Task LoadAsync()
        {
            IsLoading = true;
            try
            {
                var head = await _git.Executor.RunAsync(() => _git.GetHeadSha());
                var commits = await _git.Executor.RunAsync(() => _git.GetCommitRange(_ontoSha, head));
                foreach (var c in commits)
                    Items.Add(new RebaseTodoItem { Sha = c.Sha, Message = c.Message });
            }
            catch (Exception ex)
            {
                DialogService.ShowError("Interactive Rebase", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void Move(RebaseTodoItem item, int direction)
        {
            if (item == null) return;
            var idx = Items.IndexOf(item);
            var newIdx = idx + direction;
            if (idx < 0 || newIdx < 0 || newIdx >= Items.Count) return;
            Items.Move(idx, newIdx);
        }

        private void Start()
        {
            if (Items.Count == 0) return;
            if (Items.All(i => i.Action == RebaseTodoAction.Drop))
            {
                DialogService.ShowError("Interactive Rebase", "At least one commit must remain — not all commits can be dropped.");
                return;
            }
            RequestClose?.Invoke(true);
        }
    }
}
