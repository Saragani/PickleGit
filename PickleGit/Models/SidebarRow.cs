namespace PickleGit.Models
{
    /// <summary>
    /// Discriminates the rows of the sidebar's single flat, virtualized ListView (SidebarView.xaml).
    /// Local/remote branch folders are flattened into BranchGroup/BranchLeaf rows at build time
    /// (respecting each BranchNodeViewModel.IsExpanded) instead of relying on TreeView's own
    /// nested HierarchicalDataTemplate/VirtualizingStackPanel, which selection-highlight and
    /// resize behavior depend on.
    /// </summary>
    public enum SidebarRowKind
    {
        LocalBranchesHeader,
        RemoteBranchesHeader,
        TagsHeader,
        StashesHeader,
        RemotesHeader,
        ReflogHeader,
        PullRequestsHeader,
        SubmodulesHeader,
        WorktreesHeader,
        LocalBranchGroup,
        LocalBranchLeaf,
        RemoteBranchGroup,
        RemoteBranchLeaf,
        Tag,
        Stash,
        Remote,
        Reflog,
        PullRequest,
        Submodule,
        Worktree
    }

    /// <summary>
    /// One flattened sidebar row. IndentLevel is applied as a Margin on the row's inner content
    /// only (never on the ListViewItem container itself), so the selection/hover highlight always
    /// spans the full row width regardless of nesting depth.
    /// </summary>
    public class SidebarRow
    {
        public SidebarRowKind Kind { get; set; }
        public int IndentLevel { get; set; }

        /// <summary>
        /// The underlying model for item rows: BranchNodeViewModel (branch/group kinds), TagInfo,
        /// StashInfo, RemoteInfo, ReflogEntry, PullRequestInfo, SubmoduleInfo, or WorktreeInfo.
        /// Null for section-header rows, which have no single backing model.
        /// </summary>
        public object Payload { get; set; }

        /// <summary>
        /// Section-header expand state snapshot (branch groups instead bind live through
        /// Payload.IsExpanded on BranchNodeViewModel). Refreshed on every rebuild, which always
        /// happens immediately after a toggle, so this never goes stale in practice.
        /// </summary>
        public bool IsExpanded { get; set; }
    }
}
