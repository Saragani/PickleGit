namespace PickleGit.Models
{
    public class FileChange
    {
        public string Path { get; set; }
        public string OldPath { get; set; }
        public FileChangeKind Kind { get; set; }
        public int LinesAdded { get; set; }
        public int LinesDeleted { get; set; }
        public bool IsStaged { get; set; }

        /// <summary>For Kind == Conflicted: true when this side (ours/HEAD) has no version of the
        /// file at all — an add/delete existence conflict, not a content conflict. Git writes no
        /// conflict markers for these, so the merge editor must treat them specially instead of
        /// diffing (otherwise Ours/Theirs would render as identical, misleading content).</summary>
        public bool OursMissing { get; set; }

        /// <summary>Same as <see cref="OursMissing"/>, for the other side (theirs/MERGE_HEAD).</summary>
        public bool TheirsMissing { get; set; }

        public string StatusLabel
        {
            get
            {
                switch (Kind)
                {
                    case FileChangeKind.Added: return "A";
                    case FileChangeKind.Deleted: return "D";
                    case FileChangeKind.Modified: return "M";
                    case FileChangeKind.Renamed: return "R";
                    case FileChangeKind.Copied: return "C";
                    case FileChangeKind.Conflicted: return "!";
                    default: return "?";
                }
            }
        }
    }

    // Represents a file that was changed across multiple selected commits.
    public class AggregatedFileChange
    {
        public string Path { get; set; }
        public int CommitCount { get; set; }
        public FileChangeKind Kind { get; set; }
        public string StatusLabel
        {
            get
            {
                switch (Kind)
                {
                    case FileChangeKind.Added:    return "A";
                    case FileChangeKind.Deleted:  return "D";
                    case FileChangeKind.Modified: return "M";
                    case FileChangeKind.Renamed:  return "R";
                    case FileChangeKind.Copied:   return "C";
                    default:                      return "M";
                }
            }
        }
    }

    public enum FileChangeKind
    {
        Added,
        Deleted,
        Modified,
        Renamed,
        Copied,
        Untracked,
        Conflicted
    }

    /// <summary>Ordering applied to the Staged/Unstaged file panels — shared by both panels.
    /// <see cref="Status"/> (the default) sorts by status first, then path — matching the app's
    /// original hardcoded behavior before this became user-selectable.</summary>
    public enum FileSortMode { Status, PathAsc, PathDesc, NameAsc, NameDesc }

    /// <summary>Flat list vs folder-grouped tree — one shared setting for both the Staged and
    /// Unstaged panels.</summary>
    public enum FileViewMode { Flat, Tree }

    public enum FileTreeRowKind { Folder, File }

    /// <summary>One flattened row of a Staged/Unstaged panel in Tree view mode — either a folder
    /// group header (no <see cref="File"/>) or a leaf wrapping the real <see cref="FileChange"/>.
    /// Built by <see cref="PickleGit.Services.FileTreeBuilder"/>.</summary>
    public class FileTreeRow
    {
        public FileTreeRowKind Kind { get; set; }
        public int IndentLevel { get; set; }
        public string DisplayName { get; set; }
        public FileChange File { get; set; }

        /// <summary>Folder rows only: the folder's full relative path (e.g. "Views/Dialogs"),
        /// used as the expand/collapse state key — tracked independently per panel (Staged vs
        /// Unstaged), see RepositoryViewModel's _expandedStagedTreeFolders/_expandedWorkingTreeFolders.</summary>
        public string FullPath { get; set; }

        /// <summary>Folder rows only: whether this folder is currently expanded — collapsed
        /// (false) is the default, so a fresh tree starts fully collapsed.</summary>
        public bool IsExpanded { get; set; }
    }

    /// <summary>One entry in the Staged/Unstaged sort-mode picker — pairs the enum value with the
    /// friendly label shown in the ComboBox.</summary>
    public class FileSortModeOption
    {
        public FileSortMode Mode { get; set; }
        public string Label { get; set; }
    }

    /// <summary>Parsed diff for one file: its hunks, plus whether the comparison was binary
    /// (no hunks — the view shows a "binary file" notice instead of a blank pane).</summary>
    public class FileDiffResult
    {
        public System.Collections.Generic.List<DiffHunk> Hunks { get; set; }
            = new System.Collections.Generic.List<DiffHunk>();
        public bool IsBinary { get; set; }
    }

    public class DiffHunk : System.ComponentModel.INotifyPropertyChanged
    {
        public string Header { get; set; }
        public int OldStart { get; set; }
        public int NewStart { get; set; }
        public System.Collections.Generic.List<DiffLine> Lines { get; set; }
            = new System.Collections.Generic.List<DiffLine>();

        private bool _hasLineSelection;
        /// <summary>True when at least one of this hunk's added/deleted lines is currently selected
        /// in the diff view — drives the hunk header buttons switching between "Stage Hunk" (whole
        /// hunk) and "Stage Lines" (just the selection).</summary>
        public bool HasLineSelection
        {
            get => _hasLineSelection;
            set
            {
                if (_hasLineSelection == value) return;
                _hasLineSelection = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasLineSelection)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    public struct DiffHighlightSpan
    {
        public int Start;
        public int Length;
        public DiffHighlightSpan(int start, int length) { Start = start; Length = length; }
    }

    public class DiffLine
    {
        public string Content { get; set; }
        public DiffLineKind Kind { get; set; }
        public int? OldLineNumber { get; set; }
        public int? NewLineNumber { get; set; }

        /// <summary>Word-level intra-line diff spans (char offsets into Content), or null for none.</summary>
        public System.Collections.Generic.List<DiffHighlightSpan> HighlightSpans { get; set; }

        /// <summary>Syntax-highlighting spans (char offsets into Content), or null when the file's
        /// language isn't recognized or a span wasn't computed for this line.</summary>
        public System.Collections.Generic.List<SyntaxSpan> SyntaxSpans { get; set; }
    }

    public enum DiffLineKind { Context, Added, Deleted, Header }

    public enum DiffItemKind { HunkHeader, Line }

    /// <summary>Which content the main diff pane (DiffView) is currently showing — mutually
    /// exclusive with each other, gating which section of DiffView.xaml is visible. Blame is not a
    /// top-level mode: in History mode the commit list stays visible and IsBlameContent
    /// (RepositoryViewModel) toggles the right-hand content between a commit's diff and its blame
    /// </summary>
    public enum DiffPaneMode { Diff, History }

    public class DiffItem
    {
        public DiffItemKind Kind { get; set; }
        public string Header { get; set; }
        public DiffLine Line { get; set; }

        /// <summary>The hunk this item belongs to (header itself, or one of its lines) — used by hunk-level stage/discard actions.</summary>
        public DiffHunk Hunk { get; set; }
    }

    /// <summary>One row of the side-by-side diff view: old content on the left, new content on the right.
    /// Either side may be null (a filler row) when a line was purely added or purely deleted.</summary>
    public class SideBySideItem
    {
        public DiffItemKind Kind { get; set; }
        public string Header { get; set; }
        public DiffLine Left { get; set; }
        public DiffLine Right { get; set; }

        /// <summary>The hunk this row belongs to — enables hunk-level stage/discard from the side-by-side view.</summary>
        public DiffHunk Hunk { get; set; }
    }
}
