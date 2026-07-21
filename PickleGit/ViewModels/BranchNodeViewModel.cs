using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System;
using System.Linq;
using PickleGit.Models;

namespace PickleGit.ViewModels
{
    public class BranchNodeViewModel : INotifyPropertyChanged
    {
        public string DisplayName { get; set; }
        public string FullName { get; set; }
        public string ExpansionKey { get; set; }
        public bool IsGroup { get; set; }
        public BranchInfo BranchInfo { get; set; }
        public Action<BranchNodeViewModel> ExpansionChanged { get; set; }
        public ObservableCollection<BranchNodeViewModel> Children { get; set; }
            = new ObservableCollection<BranchNodeViewModel>();

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
                ExpansionChanged?.Invoke(this);
            }
        }

        public bool IsHead  => BranchInfo?.IsHead  ?? false;
        public int  AheadBy => BranchInfo?.AheadBy ?? 0;
        public int  BehindBy=> BranchInfo?.BehindBy?? 0;
        public bool HasAhead => AheadBy > 0;
        public bool HasBehind=> BehindBy > 0;
        public bool HasUpstream => !string.IsNullOrEmpty(BranchInfo?.TrackedBranchName);
        /// <summary>Gates the "Fetch this branch" context-menu item — only meaningful for a
        /// tracking branch that isn't the checked-out one (updating the current branch's ref while
        /// it's checked out would desync the index/working tree from HEAD; git itself refuses this).</summary>
        public bool CanFetchBranch => HasUpstream && !IsHead;

        private bool _hasOpenPr;
        /// <summary>Set by RepositoryViewModel.UpdateBranchPrBadges after the PR list loads/refreshes.</summary>
        public bool HasOpenPr
        {
            get => _hasOpenPr;
            set
            {
                if (_hasOpenPr == value) return;
                _hasOpenPr = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasOpenPr)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public static ObservableCollection<BranchNodeViewModel> Build(
            IEnumerable<BranchInfo> branches,
            string scope,
            ISet<string> collapsedKeys,
            Action<BranchNodeViewModel> expansionChanged)
        {
            var result = new ObservableCollection<BranchNodeViewModel>();

            foreach (var branch in (branches ?? Enumerable.Empty<BranchInfo>()).OrderBy(b => b.Name))
            {
                var parts = (branch.Name ?? string.Empty)
                    .Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    continue;

                var current = result;
                var path = string.Empty;

                for (int i = 0; i < parts.Length; i++)
                {
                    var isLeaf = i == parts.Length - 1;
                    path = string.IsNullOrEmpty(path) ? parts[i] : path + "/" + parts[i];

                    if (isLeaf)
                    {
                        current.Add(new BranchNodeViewModel
                        {
                            DisplayName = parts[i],
                            FullName    = branch.Name,
                            IsGroup     = false,
                            BranchInfo  = branch
                        });
                        continue;
                    }

                    var key = scope + ":" + path;
                    var group = current.FirstOrDefault(n =>
                        n.IsGroup && string.Equals(n.DisplayName, parts[i], System.StringComparison.OrdinalIgnoreCase));
                    if (group == null)
                    {
                        group = new BranchNodeViewModel
                        {
                            DisplayName = parts[i],
                            FullName = path,
                            ExpansionKey = key,
                            IsGroup = true,
                            _isExpanded = collapsedKeys == null || !collapsedKeys.Contains(key),
                            ExpansionChanged = expansionChanged
                        };
                        current.Add(group);
                    }

                    current = group.Children;
                }
            }

            return result;
        }
    }
}
