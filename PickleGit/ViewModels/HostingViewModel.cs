using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PickleGit.Models;
using PickleGit.Services;
using PickleGit.Services.Hosting;

namespace PickleGit.ViewModels
{
    /// <summary>
    /// Hosting-provider integration for one repository tab: provider detection,
    /// the open-PR list, PR badges on branch-tree nodes, and web links.
    /// Exposed as RepositoryViewModel.Hosting; reads branch/remote state from the
    /// owning VM (never mutates it beyond the per-node HasOpenPr badge flag).
    /// </summary>
    public class HostingViewModel : BaseViewModel
    {
        private readonly RepositoryViewModel _repo;

        public HostingViewModel(RepositoryViewModel repo)
        {
            _repo = repo;
            RefreshPullRequestsCommand = new RelayCommand(async () => await LoadPullRequestsAsync(), () => HasHostingProvider);
            OpenPullRequestCommand = new RelayCommand(p =>
            {
                if (p is PullRequestInfo pr && !string.IsNullOrEmpty(pr.WebUrl))
                    OpenUrl(pr.WebUrl);
            });
            CreatePullRequestCommand = new RelayCommand(CreatePullRequest, _ => HasHostingProvider && !string.IsNullOrEmpty(_repo.CurrentBranch));
            OpenRepoWebPageCommand = new RelayCommand(() =>
            {
                var url = HostingProvider?.GetRepoWebUrl();
                if (!string.IsNullOrEmpty(url)) OpenUrl(url);
            }, () => HasHostingProvider);
        }

        private ObservableCollection<PullRequestInfo> _pullRequests = new ObservableCollection<PullRequestInfo>();
        public ObservableCollection<PullRequestInfo> PullRequests { get => _pullRequests; private set => Set(ref _pullRequests, value); }

        public IHostingProvider HostingProvider { get; private set; }
        public bool HasHostingProvider => HostingProvider != null;
        public bool HostingProviderConfigured => HostingProvider?.IsConfigured == true;
        public string HostingProviderName => HostingProvider?.ProviderName;

        private bool _isLoadingPullRequests;
        public bool IsLoadingPullRequests { get => _isLoadingPullRequests; private set => Set(ref _isLoadingPullRequests, value); }

        public ICommand RefreshPullRequestsCommand { get; }
        public ICommand OpenPullRequestCommand { get; }
        public ICommand CreatePullRequestCommand { get; }
        public ICommand OpenRepoWebPageCommand { get; }

        private static void OpenUrl(string url)
        {
            // url comes verbatim from a hosting provider's API response (html_url / web_url /
            // links.html.href) — for a self-hosted provider that's a server the user configured
            // but shouldn't be trusted to only ever return http(s); reject anything else before
            // handing it to ShellExecute, which would otherwise invoke arbitrary URI-scheme handlers.
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }

        private void CreatePullRequest(object parameter)
        {
            if (HostingProvider == null) return;
            var source = (parameter as BranchInfo)?.DisplayName ?? _repo.CurrentBranch;
            if (string.IsNullOrEmpty(source) || source.StartsWith("detached", StringComparison.Ordinal)) return;

            var target = _repo.RemoteBranches
                .Select(b => b.DisplayName)
                .FirstOrDefault(n => string.Equals(n, "main", StringComparison.OrdinalIgnoreCase))
                ?? _repo.RemoteBranches.Select(b => b.DisplayName)
                .FirstOrDefault(n => string.Equals(n, "master", StringComparison.OrdinalIgnoreCase))
                ?? _repo.RemoteBranches.Select(b => b.DisplayName).FirstOrDefault(n => n != source);
            ShowCreatePullRequestDialog(source, target);
        }

        /// <summary>Invoked when a branch is dropped onto another branch — both endpoints are already known.</summary>
        public void DragCreatePullRequest(string sourceName, string targetName)
        {
            if (HostingProvider == null) return;
            var source = _repo.LocalBranches.FirstOrDefault(b => b.Name == sourceName)?.DisplayName ?? sourceName;
            var target = _repo.RemoteBranches.FirstOrDefault(b => string.Equals(b.DisplayName, targetName, StringComparison.Ordinal))?.DisplayName
                ?? targetName;
            ShowCreatePullRequestDialog(source, target);
        }

        private async void ShowCreatePullRequestDialog(string source, string defaultTarget)
        {
            var provider = HostingProvider;
            if (provider == null || string.IsNullOrEmpty(source)) return;

            // Only the checked-out branch can be pushed via PushAsync (it pushes CurrentBranch,
            // not an arbitrary branch name) — for a different, non-checked-out source branch
            // (e.g. dragged onto a target), skip the push-first prompt rather than risk pushing
            // the wrong branch.
            if (string.Equals(source, _repo.CurrentBranch, StringComparison.Ordinal))
            {
                var current = _repo.LocalBranches.FirstOrDefault(b => b.IsHead);
                if (current != null && current.AheadBy > 0)
                {
                    var word = current.AheadBy == 1 ? "commit" : "commits";
                    if (!DialogService.Confirm("Push Before Creating PR",
                            $"This branch has {current.AheadBy} unpushed {word}. Push now?", okText: "Push"))
                        return;
                    if (!await _repo.PushAsync()) return;
                }
            }

            var targets = _repo.RemoteBranches.Select(b => b.DisplayName).Distinct()
                .Where(n => !string.Equals(n, source, StringComparison.Ordinal))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            if (targets.Count == 0)
            {
                DialogService.ShowError("Create Pull Request", "No remote branch is available to use as the target.");
                return;
            }

            var dlg = new Views.Dialogs.CreatePullRequestDialog
            {
                Owner = Application.Current?.MainWindow,
                DialogTitle = "Create Pull Request",
                HeaderText = $"Create Pull Request — {provider.ProviderName}",
                SourceBranch = source,
                TargetBranches = targets,
                SelectedTargetBranch = defaultTarget != null && targets.Contains(defaultTarget) ? defaultTarget : targets[0],
                PrTitle = HumanizeBranchName(source),
                SupportsDraft = !(provider is BitbucketCloudProvider)
            };
            if (dlg.ShowDialog() != true) return;

            if (provider.IsConfigured)
            {
                try
                {
                    var created = await provider.CreatePullRequestAsync(
                        dlg.PrTitle, dlg.Description, source, dlg.SelectedTargetBranch, dlg.IsDraft);
                    if (!string.IsNullOrEmpty(created?.WebUrl)) OpenUrl(created.WebUrl);
                    await LoadPullRequestsAsync();
                }
                catch (Exception ex)
                {
                    DialogService.ShowError("Create Pull Request Failed", ex.Message);
                }
            }
            else
            {
                // No API token stored for this host — fall back to the browser compose page, pre-filled.
                OpenUrl(provider.GetCreatePrUrl(source, dlg.SelectedTargetBranch, dlg.PrTitle, dlg.Description));
            }
        }

        private static string HumanizeBranchName(string branch)
        {
            if (string.IsNullOrEmpty(branch)) return string.Empty;
            var slash = branch.LastIndexOf('/');
            var name = slash >= 0 && slash < branch.Length - 1 ? branch.Substring(slash + 1) : branch;
            name = name.Replace('-', ' ').Replace('_', ' ');
            return char.ToUpper(name[0]) + name.Substring(1);
        }

        /// <summary>Detects the hosting provider from the origin remote and loads open PRs. Safe to call repeatedly.</summary>
        public async Task LoadPullRequestsAsync()
        {
            var origin = _repo.Remotes.FirstOrDefault(r => r.Name == "origin") ?? _repo.Remotes.FirstOrDefault();
            HostingProvider = origin != null ? HostingProviderFactory.Create(origin.Url) : null;
            RaisePropertyChanged(nameof(HostingProvider));
            RaisePropertyChanged(nameof(HasHostingProvider));
            RaisePropertyChanged(nameof(HostingProviderConfigured));
            RaisePropertyChanged(nameof(HostingProviderName));

            if (HostingProvider == null)
            {
                PullRequests = new ObservableCollection<PullRequestInfo>();
                UpdateBranchPrBadges();
                return;
            }

            IsLoadingPullRequests = true;
            try
            {
                var prs = await HostingProvider.GetPullRequestsAsync();
                PullRequests = new ObservableCollection<PullRequestInfo>(prs);
            }
            catch { PullRequests = new ObservableCollection<PullRequestInfo>(); }
            finally
            {
                IsLoadingPullRequests = false;
                UpdateBranchPrBadges();
            }
        }

        /// <summary>Flags branch-tree nodes whose branch is the source of an open PR, so the sidebar can badge them.
        /// Called after every branch-tree rebuild (the nodes are fresh instances each time).</summary>
        public void UpdateBranchPrBadges()
        {
            var openSources = new HashSet<string>(
                PullRequests.Select(p => p.SourceBranch).Where(s => !string.IsNullOrEmpty(s)),
                StringComparer.OrdinalIgnoreCase);
            ApplyPrBadges(_repo.LocalBranchTree, openSources);
            ApplyPrBadges(_repo.RemoteBranchTree, openSources);
        }

        private static void ApplyPrBadges(IEnumerable<BranchNodeViewModel> nodes, HashSet<string> openSources)
        {
            if (nodes == null) return;
            foreach (var node in nodes)
            {
                node.HasOpenPr = !node.IsGroup && node.BranchInfo != null && openSources.Contains(node.BranchInfo.DisplayName);
                ApplyPrBadges(node.Children, openSources);
            }
        }
    }
}
