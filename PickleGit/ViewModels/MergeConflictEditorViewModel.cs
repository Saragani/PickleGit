using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using PickleGit.Models;
using PickleGit.Services;

namespace PickleGit.ViewModels
{
    public enum ExistenceChoice { Undecided, KeepFile, DeleteFile }

    /// <summary>One line of a conflict block's Ours/Base/Theirs side — Included drives the
    /// per-line checkbox in the two-pane merge editor. Display is a throwaway DiffLine wrapper
    /// (Content/Kind/HighlightSpans) that exists purely so the view can reuse the normal diff
    /// pipeline's rendering (Behaviors/WordDiffHighlighter, DiffLineBackgroundConverter) without
    /// any changes to either — Kind/HighlightSpans are set once by
    /// ConflictBlockViewModel.BuildLineRows after this block's line alignment is computed.</summary>
    public class ConflictLineOption : BaseViewModel
    {
        public string Text { get; }
        public DiffLine Display { get; }

        private readonly Action<ConflictLineOption> _onToggle;

        private bool _included;
        public bool Included
        {
            get => _included;
            set { if (Set(ref _included, value)) _onToggle?.Invoke(this); }
        }

        /// <summary>Position in the click order this line was checked at (1-based), or null while
        /// unchecked — CurrentText assembles included lines sorted by this, not by side-then-
        /// original-index, so clicking theirs-B then mine then theirs-A produces exactly that
        /// order in the result. Set by the owning ConflictBlockViewModel, never by this class
        /// itself, since bulk operations (a pane's "select all" checkbox) need to assign a whole
        /// batch of these against one shared counter.</summary>
        private int? _pickOrder;
        public int? PickOrder { get => _pickOrder; set => Set(ref _pickOrder, value); }

        /// <summary>Flips Included — bound to a click anywhere on the line's row (not just its
        /// hover glyph), and reused as-is by the Result pane's hover "remove" affordance (a line
        /// shown there is always currently Included, so toggling it there always means "remove").</summary>
        public ICommand ToggleCommand { get; }

        public ConflictLineOption(string text, Action<ConflictLineOption> onToggle)
        {
            Text = text;
            _onToggle = onToggle;
            Display = new DiffLine { Content = text, Kind = DiffLineKind.Context };
            ToggleCommand = new RelayCommand(() => Included = !Included);
        }

        /// <summary>Sets Included without firing onToggle — used by bulk operations (Select All
        /// Mine/Theirs/Both, Reset) that recompute the block once at the end instead of once per
        /// line touched.</summary>
        public void SetIncludedSilently(bool value) => Set(ref _included, value);
    }

    public enum ConflictPaneRowKind { Context, BlockToolbar, BlockBaseLine, BlockLine }

    /// <summary>One row of the two synced Ours/Theirs pane ListViews (see
    /// MergeConflictEditorWindow.xaml) — mirrors the existing SideBySideItem pattern
    /// (Models/RepositoryAccount.cs): both panes bind to the same collection, each with its own
    /// DataTemplateSelector projecting the Left or Right side. BlockToolbar/BlockBaseLine rows
    /// render identical content in both panes (the same duplication convention already used for
    /// hunk-header rows in the normal side-by-side diff view).</summary>
    public class ConflictPaneItem
    {
        // Properties, not fields — WPF's Binding/PropertyPath only resolves properties; a plain
        // field binds to nothing (no exception, just an always-unset value, which for e.g.
        // Visibility silently falls back to its own default of Visible).
        public ConflictPaneRowKind Kind { get; set; }
        public string ContextText { get; set; }              // Context only — one line's text
        public int? ContextOldLineNumber { get; set; }        // Context only
        public int? ContextNewLineNumber { get; set; }        // Context only
        public ConflictBlockViewModel BlockVm { get; set; }
        public ConflictLineOption BaseLine { get; set; }    // BlockBaseLine only
        public ConflictLineOption LeftLine { get; set; }    // BlockLine only — Ours side, null = filler
        public ConflictLineOption RightLine { get; set; }   // BlockLine only — Theirs side, null = filler
    }

    public enum ConflictResultRowKind
    {
        Context,            // numbered, plain context line
        ResolvedLine,       // numbered, individually removable (click unchecks it back in its source pane)
        ResolvedBaseLabel,  // unnumbered small label — UseBaseVerbatim or nothing-picked-but-touched
        ResolvedBaseLine,   // numbered content line under a ResolvedBaseLabel
        DefaultBaseLabel,   // unnumbered small label — untouched block defaulting to base
        DefaultBaseLine,    // numbered content line under a DefaultBaseLabel
        Unresolved          // unnumbered placeholder — no default exists, still blocking
    }

    /// <summary>One row of the Result pane — same per-line, numbered shape as the Ours/Theirs
    /// panes (LineNumber/LineText), plus the removable per-line case (ResolvedLine — hovering
    /// shows a red "−" that unchecks SourceLine back in its source pane, via the same
    /// ConflictLineOption.ToggleCommand the pane itself uses). ResolvedBase*/DefaultBase* split
    /// across two row kinds each: one small unnumbered label row per block stating why (used base
    /// verbatim / nothing picked yet), followed by that block's own numbered content lines — no
    /// single source ConflictLineOption exists for those lines (a whole base/verbatim block isn't
    /// tied to individual picks), so they're numbered but not individually removable.</summary>
    public class ConflictResultItem
    {
        public ConflictResultRowKind Kind { get; set; }
        public int? LineNumber { get; set; }                 // all kinds except the *Label/Unresolved rows
        public string LineText { get; set; }                 // Context/ResolvedBaseLine/DefaultBaseLine
        public ConflictBlockViewModel BlockVm { get; set; }
        public ConflictLineOption SourceLine { get; set; }    // ResolvedLine only
    }

    /// <summary>
    /// One &lt;&lt;&lt;&lt;&lt;&lt;&lt;/=======/&gt;&gt;&gt;&gt;&gt;&gt;&gt; block's live resolution state — true per-line
    /// picking, not just a whole-block choice. OursOptions/TheirsOptions carry one
    /// ConflictLineOption per source line (checkbox-bound); BaseOptions is display-only (no per-
    /// line picking for the diff3 base section — "Use Base" is a single whole-block escape
    /// hatch instead, see UseBaseCommand). Touched flips true on the FIRST interaction of any
    /// kind (a single checkbox, or one of the bulk buttons) and stays true even if that leaves
    /// zero lines included — an intentionally-empty result (deleting the disputed content
    /// entirely) is a valid resolution, distinct from "never looked at this block".
    /// </summary>
    public class ConflictBlockViewModel : BaseViewModel
    {
        public MergeConflictBlock Block { get; }
        private readonly string _newline;
        private readonly Action _onChanged;

        public List<ConflictLineOption> OursOptions { get; }
        public List<ConflictLineOption> TheirsOptions { get; }
        public List<ConflictLineOption> BaseOptions { get; }

        /// <summary>Pre-built BlockLine rows for the two-pane view — computed once at
        /// construction via LcsAligner (line content never changes after parsing, only Included
        /// state does), so this never needs to be rebuilt when the user toggles a checkbox.</summary>
        public List<ConflictPaneItem> LineRows { get; }

        private bool _touched;
        public bool Touched { get => _touched; private set => Set(ref _touched, value); }

        private bool _useBaseVerbatim;
        public bool UseBaseVerbatim { get => _useBaseVerbatim; private set => Set(ref _useBaseVerbatim, value); }

        public bool HasBase => !string.IsNullOrEmpty(Block.BaseText);

        /// <summary>True once this block has a real, save-ready resolution — either an explicit
        /// one (Touched) or, when there's no explicit pick yet but a diff3 base exists, the
        /// implicit "do nothing = keep base" default (matching GitKraken: an untouched hunk with
        /// a common ancestor never blocks completing the merge, it just previews as base content,
        /// shown in the Result pane with distinct styling — see ConflictResultRowKind.DefaultBase).
        /// A block with no base and no explicit pick has no sensible default and stays blocking.</summary>
        public bool IsResolvedEffective => Touched || HasBase;

        /// <summary>Bound to a checkbox above the Ours pane's toolbar for this block — GitKraken's
        /// "one checkbox per hunk" per pane. Checking it includes every Ours line (in original
        /// order); unchecking clears all of them. Independent of TheirsOptions/AllTheirsChecked —
        /// each pane's bulk toggle only ever touches its own side, so building a custom mix by
        /// checking some individual lines on top of "all mine" still works as expected.</summary>
        public bool AllOursChecked
        {
            get => OursOptions.Count > 0 && OursOptions.All(o => o.Included);
            set => SetAllOurs(value);
        }

        public bool AllTheirsChecked
        {
            get => TheirsOptions.Count > 0 && TheirsOptions.All(o => o.Included);
            set => SetAllTheirs(value);
        }

        public ICommand UseBaseCommand { get; }
        public ICommand ResetCommand { get; }

        /// <summary>Every currently-included line (Ours and Theirs together), in the order they
        /// were actually checked — not a fixed "all mine then all theirs" grouping. Clicking
        /// theirs-B, then mine, then theirs-A produces exactly that order here. Backs both
        /// CurrentText and the Result pane's per-line rows (MergeConflictFileViewModel.
        /// RebuildPaneAndResultItems).</summary>
        public IEnumerable<ConflictLineOption> OrderedIncluded =>
            OursOptions.Concat(TheirsOptions)
                .Where(o => o.Included && o.PickOrder.HasValue)
                .OrderBy(o => o.PickOrder.Value);

        /// <summary>The text this block currently contributes to the file's ResultText — either
        /// the base text verbatim (UseBaseVerbatim), or OrderedIncluded joined by line.</summary>
        public string CurrentText => UseBaseVerbatim
            ? (Block.BaseText ?? string.Empty)
            : string.Join(_newline, OrderedIncluded.Select(o => o.Text));

        public string ResolvedSummary => !Touched ? null
            : UseBaseVerbatim ? "Resolved — used base"
            : $"Resolved — {OursOptions.Count(o => o.Included)} line(s) mine, {TheirsOptions.Count(o => o.Included)} line(s) theirs";

        private int _nextPickOrder;

        public ConflictBlockViewModel(MergeConflictBlock block, string newline, Action onChanged)
        {
            Block = block;
            _newline = newline;
            _onChanged = onChanged;

            OursOptions = block.OursLines.Select(l => new ConflictLineOption(l, OnLineToggled)).ToList();
            TheirsOptions = block.TheirsLines.Select(l => new ConflictLineOption(l, OnLineToggled)).ToList();
            BaseOptions = block.BaseLines.Select(l => new ConflictLineOption(l, null)).ToList();

            LineRows = BuildLineRows();

            UseBaseCommand = new RelayCommand(() =>
            {
                ClearAllPicks();
                UseBaseVerbatim = true;
                Touched = true;
                Commit();
            }, () => HasBase);
            ResetCommand = new RelayCommand(() =>
            {
                ClearAllPicks();
                UseBaseVerbatim = false;
                Touched = false;
                Commit();
            });
        }

        private void ClearAllPicks()
        {
            foreach (var o in OursOptions) { o.SetIncludedSilently(false); o.PickOrder = null; }
            foreach (var o in TheirsOptions) { o.SetIncludedSilently(false); o.PickOrder = null; }
            _nextPickOrder = 0;
        }

        private void OnLineToggled(ConflictLineOption opt)
        {
            opt.PickOrder = opt.Included ? ++_nextPickOrder : (int?)null;
            UseBaseVerbatim = false;
            SettleTouched();
            Commit();
        }

        /// <summary>Stamps each line's gutter number (file-relative, like a normal diff's Old/New
        /// line numbers) — called on every rebuild by MergeConflictFileViewModel.
        /// RebuildPaneAndResultItems, which tracks the running Ours/Theirs line counters across
        /// the whole file (context runs advance both counters identically; each block advances
        /// them by its own Ours/Theirs line count, which can differ between blocks).</summary>
        public void AssignLineNumbers(int oursStart, int theirsStart)
        {
            for (int i = 0; i < OursOptions.Count; i++) OursOptions[i].Display.OldLineNumber = oursStart + i;
            for (int i = 0; i < TheirsOptions.Count; i++) TheirsOptions[i].Display.NewLineNumber = theirsStart + i;
        }

        /// <summary>Backs AllOursChecked's setter — bulk-include/exclude every Ours line, in
        /// original order, leaving TheirsOptions untouched.</summary>
        public void SetAllOurs(bool include)
        {
            foreach (var o in OursOptions)
            {
                o.SetIncludedSilently(include);
                o.PickOrder = include ? ++_nextPickOrder : (int?)null;
            }
            UseBaseVerbatim = false;
            SettleTouched();
            Commit();
        }

        public void SetAllTheirs(bool include)
        {
            foreach (var o in TheirsOptions)
            {
                o.SetIncludedSilently(include);
                o.PickOrder = include ? ++_nextPickOrder : (int?)null;
            }
            UseBaseVerbatim = false;
            SettleTouched();
            Commit();
        }

        /// <summary>Touched tracks "does this block have a real resolution", not "has the user
        /// ever clicked anything" — picking a line then unpicking it back to zero must read as
        /// unresolved again, not as a deliberate "resolve to empty content" (that still exists,
        /// just as an explicit escape hatch: Reset, or checking/unchecking down to zero, are the
        /// same end state). Only UseBaseVerbatim (a real, explicit choice) can leave a block
        /// Touched with zero Ours/Theirs lines included.</summary>
        private void SettleTouched()
        {
            bool anyIncluded = OursOptions.Any(o => o.Included) || TheirsOptions.Any(o => o.Included);
            Touched = UseBaseVerbatim || anyIncluded;
            if (!Touched) _nextPickOrder = 0;
        }

        private void Commit()
        {
            RaisePropertyChanged(nameof(CurrentText));
            RaisePropertyChanged(nameof(ResolvedSummary));
            RaisePropertyChanged(nameof(AllOursChecked));
            RaisePropertyChanged(nameof(AllTheirsChecked));
            _onChanged();
        }

        /// <summary>Pairs Ours lines against Theirs lines via LcsAligner for display, then groups
        /// consecutive unmatched runs on each side into 1:1 "changed" rows (word-highlighted) the
        /// same way GitService.ComputeWordDiffsForHunk pairs adjacent deleted/added line runs —
        /// any leftover longer side becomes filler rows. Every real line in the block — matched or
        /// not — gets the block's flat Deleted/Added tint (GitKraken colors a whole conflict hunk
        /// solidly per side rather than per-line; using Context for a matched/identical pair here
        /// left it with no background at all, an invisible-looking gap inside an otherwise-colored
        /// hunk).</summary>
        private List<ConflictPaneItem> BuildLineRows()
        {
            var oursTexts = OursOptions.Select(o => o.Text).ToList();
            var theirsTexts = TheirsOptions.Select(o => o.Text).ToList();
            var pairs = LcsAligner.Align(oursTexts, theirsTexts);
            var rows = new List<ConflictPaneItem>();

            int i = 0;
            while (i < pairs.Count)
            {
                var p = pairs[i];
                if (p.Matched)
                {
                    OursOptions[p.LeftIndex].Display.Kind = DiffLineKind.Deleted;
                    TheirsOptions[p.RightIndex].Display.Kind = DiffLineKind.Added;
                    rows.Add(new ConflictPaneItem
                    {
                        Kind = ConflictPaneRowKind.BlockLine, BlockVm = this,
                        LeftLine = OursOptions[p.LeftIndex], RightLine = TheirsOptions[p.RightIndex]
                    });
                    i++;
                    continue;
                }

                var leftRun = new List<int>();
                var rightRun = new List<int>();
                while (i < pairs.Count && !pairs[i].Matched)
                {
                    if (pairs[i].LeftIndex >= 0) leftRun.Add(pairs[i].LeftIndex);
                    else rightRun.Add(pairs[i].RightIndex);
                    i++;
                }

                int pairCount = Math.Min(leftRun.Count, rightRun.Count);
                for (int k = 0; k < pairCount; k++)
                {
                    var lo = OursOptions[leftRun[k]];
                    var ro = TheirsOptions[rightRun[k]];
                    lo.Display.Kind = DiffLineKind.Deleted;
                    ro.Display.Kind = DiffLineKind.Added;
                    var (lh, rh) = LcsAligner.DiffWords(lo.Text, ro.Text);
                    lo.Display.HighlightSpans = lh;
                    ro.Display.HighlightSpans = rh;
                    rows.Add(new ConflictPaneItem { Kind = ConflictPaneRowKind.BlockLine, BlockVm = this, LeftLine = lo, RightLine = ro });
                }
                for (int k = pairCount; k < leftRun.Count; k++)
                {
                    OursOptions[leftRun[k]].Display.Kind = DiffLineKind.Deleted;
                    rows.Add(new ConflictPaneItem { Kind = ConflictPaneRowKind.BlockLine, BlockVm = this, LeftLine = OursOptions[leftRun[k]], RightLine = null });
                }
                for (int k = pairCount; k < rightRun.Count; k++)
                {
                    TheirsOptions[rightRun[k]].Display.Kind = DiffLineKind.Added;
                    rows.Add(new ConflictPaneItem { Kind = ConflictPaneRowKind.BlockLine, BlockVm = this, LeftLine = null, RightLine = TheirsOptions[rightRun[k]] });
                }
            }
            return rows;
        }
    }

    /// <summary>
    /// One conflicted file's merge state. For a content conflict, parses marker blocks into
    /// per-block ConflictBlockViewModels with true per-line picking, and exposes two flattened
    /// views built from the same MergeConflictDocument.Items order: PaneItems (the two-pane Ours/
    /// Theirs editor) and ResultItems (the read-only live-preview Result pane). For an add/delete
    /// existence conflict (no marker content — see FileChange.OursMissing/TheirsMissing), there
    /// is no document to parse; the file is resolved by an explicit Keep/Delete choice instead.
    /// </summary>
    public class MergeConflictFileViewModel : BaseViewModel
    {
        private readonly string _absolutePath;
        private readonly MergeConflictDocument _doc;
        private readonly System.Text.Encoding _encoding;
        private readonly List<ConflictBlockViewModel> _blockVms = new List<ConflictBlockViewModel>();
        private readonly Dictionary<MergeConflictBlock, ConflictBlockViewModel> _blockVmByBlock =
            new Dictionary<MergeConflictBlock, ConflictBlockViewModel>();

        public string RelativePath { get; }
        public bool IsExistenceConflict { get; }
        public bool OursMissing { get; }
        public bool TheirsMissing { get; }

        /// <summary>Human-readable reason shown in the existence-conflict banner.</summary>
        public string ExistenceMessage => OursMissing
            ? "This file does not exist on your side of the merge (deleted, or added only on theirs)."
            : "This file does not exist on their side of the merge (deleted, or added only on yours).";

        private string _resultText;
        /// <summary>The live merged buffer, rebuilt from scratch on every change (see
        /// RebuildResultText) — not shown directly; ResultItems is what the Result pane binds to,
        /// this is what Save() writes to disk.</summary>
        public string ResultText { get => _resultText; private set => Set(ref _resultText, value); }

        public int UnresolvedCount => _blockVms.Count(b => !b.IsResolvedEffective);

        public string BlockStatusLabel => _doc == null ? null
            : $"{_blockVms.Count(b => b.IsResolvedEffective)} of {_blockVms.Count} conflict(s) resolved";

        private ObservableCollection<ConflictPaneItem> _paneItems = new ObservableCollection<ConflictPaneItem>();
        public ObservableCollection<ConflictPaneItem> PaneItems { get => _paneItems; private set => Set(ref _paneItems, value); }

        private ObservableCollection<ConflictResultItem> _resultItems = new ObservableCollection<ConflictResultItem>();
        public ObservableCollection<ConflictResultItem> ResultItems { get => _resultItems; private set => Set(ref _resultItems, value); }

        private ExistenceChoice _existenceChoice;
        public ExistenceChoice ExistenceChoice
        {
            get => _existenceChoice;
            set
            {
                if (!Set(ref _existenceChoice, value)) return;
                RaisePropertyChanged(nameof(IsResolved));
                RaisePropertyChanged(nameof(KeepFileChecked));
                RaisePropertyChanged(nameof(DeleteFileChecked));
            }
        }

        public bool KeepFileChecked => ExistenceChoice == ExistenceChoice.KeepFile;
        public bool DeleteFileChecked => ExistenceChoice == ExistenceChoice.DeleteFile;

        public bool IsResolved => IsExistenceConflict
            ? ExistenceChoice != ExistenceChoice.Undecided
            : UnresolvedCount == 0;

        public ICommand KeepFileCommand { get; }
        public ICommand DeleteFileCommand { get; }

        /// <summary>Whole-file "select all mine"/"select all theirs" checkbox, shown above each
        /// pane (GitKraken has one of these per pane too, above its per-hunk checkboxes) — applies
        /// SetAllOurs/SetAllTheirs to every block in the file at once.</summary>
        public bool AllMineCheckedWholeFile
        {
            get => _blockVms.Count > 0 && _blockVms.All(b => b.AllOursChecked);
            set { foreach (var b in _blockVms) b.SetAllOurs(value); }
        }

        public bool AllTheirsCheckedWholeFile
        {
            get => _blockVms.Count > 0 && _blockVms.All(b => b.AllTheirsChecked);
            set { foreach (var b in _blockVms) b.SetAllTheirs(value); }
        }

        /// <summary>Same for every file in the session — see MergeConflictSessionViewModel,
        /// which computes these once and passes them to each file VM it constructs, so the
        /// per-pane header can bind directly off CurrentFile without reaching back up to the
        /// session VM's own DataContext.</summary>
        public string OursCommitLabel { get; }
        public string OursCommitTooltip { get; }
        public string TheirsCommitLabel { get; }
        public string TheirsCommitTooltip { get; }

        public List<ConflictBlockViewModel> UnresolvedBlockVms => _blockVms.Where(b => !b.Touched).ToList();

        private int _currentUnresolvedIndex;
        public int CurrentUnresolvedIndex { get => _currentUnresolvedIndex; private set => Set(ref _currentUnresolvedIndex, value); }

        public string ConflictNavLabel
        {
            get
            {
                var count = UnresolvedBlockVms.Count;
                return count == 0 ? "No unresolved conflicts" : $"Conflict {CurrentUnresolvedIndex + 1} of {count}";
            }
        }

        public ICommand NextConflictCommand { get; }
        public ICommand PrevConflictCommand { get; }

        /// <summary>Raised by Prev/Next navigation — the window's code-behind ScrollIntoView()s
        /// the corresponding row in both pane ListViews and the Result ListView.</summary>
        public event Action<MergeConflictBlock> ScrollToBlockRequested;

        public MergeConflictFileViewModel(string absolutePath, string relativePath, bool oursMissing, bool theirsMissing,
            string oursCommitLabel = null, string oursCommitTooltip = null,
            string theirsCommitLabel = null, string theirsCommitTooltip = null,
            string gitAncestorText = null)
        {
            _absolutePath = absolutePath;
            RelativePath = relativePath;
            OursMissing = oursMissing;
            TheirsMissing = theirsMissing;
            IsExistenceConflict = oursMissing || theirsMissing;
            OursCommitLabel = oursCommitLabel;
            OursCommitTooltip = oursCommitTooltip;
            TheirsCommitLabel = theirsCommitLabel;
            TheirsCommitTooltip = theirsCommitTooltip;

            KeepFileCommand = new RelayCommand(() => ExistenceChoice = ExistenceChoice.KeepFile);
            DeleteFileCommand = new RelayCommand(() => ExistenceChoice = ExistenceChoice.DeleteFile);
            NextConflictCommand = new RelayCommand(() => GoToConflict(1), () => CurrentUnresolvedIndex < UnresolvedBlockVms.Count - 1);
            PrevConflictCommand = new RelayCommand(() => GoToConflict(-1), () => CurrentUnresolvedIndex > 0);

            if (IsExistenceConflict) return; // nothing to parse — resolved by keep/delete choice alone

            if (!File.Exists(absolutePath))
            {
                // Defensive: status reported a content conflict, but the file is gone from disk
                // (e.g. deleted externally between the status read and opening this editor) —
                // there's nothing to show or resolve here rather than throwing.
                _doc = new MergeConflictDocument();
                _resultText = string.Empty;
                RebuildPaneAndResultItems();
                return;
            }

            // Preserve the file's encoding: honor a BOM if present, otherwise read and write back
            // as BOM-less UTF-8 (never introduce a BOM the file didn't have).
            string content;
            using (var reader = new StreamReader(absolutePath,
                new System.Text.UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
            {
                content = reader.ReadToEnd();
                _encoding = reader.CurrentEncoding;
            }
            _doc = MergeConflictParser.Parse(content);
            if (gitAncestorText != null) ApplyGitAncestorText(gitAncestorText);
            foreach (var block in _doc.Blocks)
            {
                var bvm = new ConflictBlockViewModel(block, _doc.Newline, RebuildResultText);
                _blockVms.Add(bvm);
                _blockVmByBlock[block] = bvm;
            }
            RebuildResultText();
        }

        /// <summary>Fills in Block.BaseLines/BaseText for any block that doesn't already carry base
        /// data (the working file's own markers aren't diff3-style) by slicing the real common-
        /// ancestor blob (fetched via CliGitService.GetConflictAncestorTextAsync) using the working
        /// file's own context runs as sync anchors — context text is unchanged between Ours and
        /// Theirs by construction, so it usually also appears verbatim in the ancestor, in the same
        /// order. Walking _doc.Items and the ancestor text in lockstep, each Block item's own slice
        /// runs from the current position up to wherever the *next* Context item's text is found.
        ///
        /// "Context" here means Ours-equals-Theirs, not Ours-equals-Theirs-equals-ancestor — two
        /// branches can independently restructure a conflicting region and coincidentally land on
        /// identical trailing text (e.g. both extracting a variable and both ending with the same
        /// "return result;") that the real ancestor never had in that form at all. When that happens
        /// the very next Context item genuinely isn't findable in the ancestor from here, and there's
        /// no way to bound the block right before it either — both are left without base data. Rather
        /// than aborting the rest of the file over that one ambiguous region, the position is left
        /// exactly where it was (not advanced) and the search looks ahead to whichever *later*
        /// Context item's text CAN be found from that same position, resyncing there and resuming
        /// normal per-block extraction for everything after — so one ambiguous hunk costs only
        /// itself, not every later hunk in the same file.
        ///
        /// An earlier version instead re-derived diff3 markers independently (via `git merge-file`
        /// on the extracted blobs) and matched blocks by position — rejected after finding it drew a
        /// hunk boundary a line off from the original merge on a real file for exactly this reason,
        /// and by matching whole blocks instead of anchoring on context, one such mismatch made the
        /// safety check (requiring the two independently-derived blocks to agree exactly) refuse
        /// nearly every block in the file rather than just the genuinely ambiguous one.</summary>
        private void ApplyGitAncestorText(string ancestorText)
        {
            var ancestor = ancestorText.Replace("\r\n", "\n");
            var items = _doc.Items;
            int cursor = 0;
            int i = 0;
            while (i < items.Count)
            {
                var item = items[i];
                if (item.Kind == ConflictDocItemKind.Context)
                {
                    var contextNormalized = item.ContextText.Replace("\r\n", "\n");
                    if (contextNormalized.Length == 0) { i++; continue; }
                    int idx = ancestor.IndexOf(contextNormalized, cursor, StringComparison.Ordinal);
                    if (idx >= 0) { cursor = idx + contextNormalized.Length; i++; continue; }

                    // Unfindable from here — look ahead for the next Context item (anywhere later
                    // in the file) whose text CAN be found from this same position, and jump there.
                    // Everything strictly between here and there (including this Context and any
                    // Block(s) in between) is left without base data — there's no reliable bound for
                    // any of it — but resuming past a found anchor still lets later blocks recover.
                    int resyncAtIndex = -1, resyncCursor = -1;
                    for (int j = i + 1; j < items.Count; j++)
                    {
                        if (items[j].Kind != ConflictDocItemKind.Context) continue;
                        var laterContext = items[j].ContextText.Replace("\r\n", "\n");
                        if (laterContext.Length == 0) continue;
                        int laterIdx = ancestor.IndexOf(laterContext, cursor, StringComparison.Ordinal);
                        if (laterIdx < 0) continue;
                        resyncAtIndex = j;
                        resyncCursor = laterIdx + laterContext.Length;
                        break;
                    }
                    if (resyncAtIndex < 0) return; // nothing later is findable either — genuinely lost for the rest of the file
                    cursor = resyncCursor;
                    i = resyncAtIndex + 1;
                    continue;
                }

                // Block item — only fill it in when the very next item is a Context whose text is
                // actually findable from here; anything else (adjacent blocks with nothing unchanged
                // between them, this being the last item, or an unfindable next-context that the
                // lookup above will resync past on its own next iteration) just leaves it unfilled.
                int end = -1;
                if (i + 1 >= items.Count) end = ancestor.Length;
                else if (items[i + 1].Kind == ConflictDocItemKind.Context)
                {
                    var nextContext = items[i + 1].ContextText.Replace("\r\n", "\n");
                    end = nextContext.Length == 0 ? cursor : ancestor.IndexOf(nextContext, cursor, StringComparison.Ordinal);
                }

                if (end >= cursor)
                {
                    if (string.IsNullOrEmpty(item.Block.BaseText) && end > cursor)
                    {
                        var slice = ancestor.Substring(cursor, end - cursor);
                        if (slice.EndsWith("\n")) slice = slice.Substring(0, slice.Length - 1);
                        item.Block.BaseLines = slice.Length == 0
                            ? new List<string>()
                            : new List<string>(slice.Split('\n'));
                        item.Block.BaseText = slice.Length == 0 ? null : slice;
                    }
                    cursor = end;
                }
                i++;
            }
        }

        private void GoToConflict(int delta)
        {
            var list = UnresolvedBlockVms;
            if (list.Count == 0) return;
            CurrentUnresolvedIndex = Math.Max(0, Math.Min(list.Count - 1, CurrentUnresolvedIndex + delta));
            RaisePropertyChanged(nameof(ConflictNavLabel));
            ScrollToBlockRequested?.Invoke(list[CurrentUnresolvedIndex].Block);
        }

        /// <summary>Rebuilds ResultText from scratch by walking _doc.Items in original order —
        /// replaces the old single-shot substring-splice Resolve() entirely, which needed fragile
        /// "Nth occurrence of identical raw text" disambiguation whenever two blocks happened to
        /// share the same marker text. A full rebuild has no such edge case and a file's conflict
        /// count is always small enough that this costs nothing, even on every checkbox toggle.
        /// A block that resolves to zero included lines (UseBaseVerbatim=false, nothing checked —
        /// a valid "delete this content entirely" resolution) contributes neither text nor a
        /// separator, so it doesn't leave a stray blank line behind. An untouched block with a
        /// base contributes that base text (see ConflictBlockViewModel.IsResolvedEffective) —
        /// only an untouched block with NO base still falls back to its literal raw markers.</summary>
        private void RebuildResultText()
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (var item in _doc.Items)
            {
                string text;
                if (item.Kind == ConflictDocItemKind.Context)
                {
                    text = item.ContextText;
                }
                else
                {
                    var bvm = _blockVmByBlock[item.Block];
                    text = bvm.Touched ? bvm.CurrentText : bvm.HasBase ? item.Block.BaseText : item.Block.RawText;
                    if (string.IsNullOrEmpty(text)) continue;
                }
                if (!first) sb.Append(_doc.Newline);
                sb.Append(text);
                first = false;
            }
            ResultText = sb.ToString();

            RaisePropertyChanged(nameof(UnresolvedCount));
            RaisePropertyChanged(nameof(BlockStatusLabel));
            RaisePropertyChanged(nameof(IsResolved));
            RaisePropertyChanged(nameof(AllMineCheckedWholeFile));
            RaisePropertyChanged(nameof(AllTheirsCheckedWholeFile));

            var list = UnresolvedBlockVms;
            if (CurrentUnresolvedIndex >= list.Count) CurrentUnresolvedIndex = Math.Max(0, list.Count - 1);
            RaisePropertyChanged(nameof(ConflictNavLabel));

            RebuildPaneAndResultItems();
            CommandManager.InvalidateRequerySuggested();
        }

        private void RebuildPaneAndResultItems()
        {
            var pane = new List<ConflictPaneItem>();
            var result = new List<ConflictResultItem>();
            int oursLine = 1, theirsLine = 1, resultLine = 1;

            foreach (var item in _doc.Items)
            {
                if (item.Kind == ConflictDocItemKind.Context)
                {
                    // Split into individual lines (not one blob per run) so every pane row gets
                    // its own gutter number, matching GitKraken's numbered-every-line panes — the
                    // Result pane gets the exact same per-line/numbered treatment (its own running
                    // count, since the assembled output's line count generally differs from
                    // either source's).
                    var lines = item.ContextText.Split(new[] { _doc.Newline }, StringSplitOptions.None);
                    foreach (var lineText in lines)
                    {
                        pane.Add(new ConflictPaneItem
                        {
                            Kind = ConflictPaneRowKind.Context, ContextText = lineText,
                            ContextOldLineNumber = oursLine, ContextNewLineNumber = theirsLine
                        });
                        oursLine++; theirsLine++;
                        result.Add(new ConflictResultItem { Kind = ConflictResultRowKind.Context, LineText = lineText, LineNumber = resultLine++ });
                    }
                    continue;
                }

                var bvm = _blockVmByBlock[item.Block];
                bvm.AssignLineNumbers(oursLine, theirsLine);
                oursLine += bvm.OursOptions.Count;
                theirsLine += bvm.TheirsOptions.Count;

                pane.Add(new ConflictPaneItem { Kind = ConflictPaneRowKind.BlockToolbar, BlockVm = bvm });
                foreach (var baseOpt in bvm.BaseOptions)
                    pane.Add(new ConflictPaneItem { Kind = ConflictPaneRowKind.BlockBaseLine, BlockVm = bvm, BaseLine = baseOpt });
                pane.AddRange(bvm.LineRows);

                if (!bvm.Touched)
                {
                    // No explicit pick yet: a block with a diff3 base defaults to that (matching
                    // GitKraken — an untouched hunk never blocks completing the merge as long as
                    // there's a sensible default), previewed with distinct styling, not treated
                    // as blocking. Only a block with no base at all has nothing to default to.
                    if (bvm.HasBase)
                    {
                        result.Add(new ConflictResultItem { Kind = ConflictResultRowKind.DefaultBaseLabel, BlockVm = bvm });
                        foreach (var lineText in bvm.Block.BaseLines)
                            result.Add(new ConflictResultItem { Kind = ConflictResultRowKind.DefaultBaseLine, BlockVm = bvm, LineText = lineText, LineNumber = resultLine++ });
                    }
                    else
                    {
                        result.Add(new ConflictResultItem { Kind = ConflictResultRowKind.Unresolved, BlockVm = bvm });
                    }
                }
                else if (bvm.UseBaseVerbatim || !bvm.OrderedIncluded.Any())
                {
                    // Whole-base-text or nothing-picked: no single source line to hover-remove,
                    // so these are numbered but not individually removable (unlike ResolvedLine).
                    result.Add(new ConflictResultItem { Kind = ConflictResultRowKind.ResolvedBaseLabel, BlockVm = bvm });
                    var contentLines = string.IsNullOrEmpty(bvm.CurrentText)
                        ? Array.Empty<string>() : bvm.CurrentText.Split(new[] { _doc.Newline }, StringSplitOptions.None);
                    foreach (var lineText in contentLines)
                        result.Add(new ConflictResultItem { Kind = ConflictResultRowKind.ResolvedBaseLine, BlockVm = bvm, LineText = lineText, LineNumber = resultLine++ });
                }
                else
                {
                    foreach (var opt in bvm.OrderedIncluded)
                        result.Add(new ConflictResultItem { Kind = ConflictResultRowKind.ResolvedLine, BlockVm = bvm, SourceLine = opt, LineNumber = resultLine++ });
                }
            }
            PaneItems = new ObservableCollection<ConflictPaneItem>(pane);
            ResultItems = new ObservableCollection<ConflictResultItem>(result);
        }

        /// <summary>Writes the resolution to disk (content conflict) or performs the keep/delete
        /// choice on the working-tree file (existence conflict). Returns false without writing
        /// anything when the file isn't actually resolved yet, unless <paramref name="force"/> is
        /// set (used to save a content conflict that still has unresolved blocks, after the
        /// caller has confirmed that with the user).</summary>
        public bool Save(bool force = false)
        {
            if (IsExistenceConflict)
            {
                if (ExistenceChoice == ExistenceChoice.Undecided) return false;
                if (ExistenceChoice == ExistenceChoice.DeleteFile && File.Exists(_absolutePath))
                    File.Delete(_absolutePath);
                // KeepFile: leave whatever checkout already put in the working tree untouched —
                // it's staged as-is by the caller's subsequent `git add`.
                return true;
            }
            if (UnresolvedCount > 0 && !force) return false;
            File.WriteAllText(_absolutePath, ResultText, _encoding);
            return true;
        }
    }

    /// <summary>One row in the merge session's file list (left pane).</summary>
    public class ConflictFileListEntry : BaseViewModel
    {
        public FileChange FileChange { get; }
        public string Path => FileChange.Path;
        public bool IsExistenceConflict => FileChange.OursMissing || FileChange.TheirsMissing;

        /// <summary>Short chip text for the file list row; null for an ordinary content conflict.</summary>
        public string ExistenceBadge =>
            FileChange.OursMissing ? "missing (yours)" :
            FileChange.TheirsMissing ? "missing (theirs)" : null;

        private bool _isResolved;
        public bool IsResolved { get => _isResolved; set => Set(ref _isResolved, value); }

        public ConflictFileListEntry(FileChange fc) => FileChange = fc;
    }

    /// <summary>
    /// Backs Views/MergeConflictEditorWindow.xaml — a multi-file merge-conflict resolver for the
    /// whole in-progress merge/rebase/cherry-pick, not just one file: a left-hand file list (with
    /// resolved/unresolved status and an existence-conflict badge where applicable) drives a
    /// right-hand MergeConflictFileViewModel. Saving one file stages it immediately and advances to
    /// the next unresolved file, so the whole session can be worked through without closing and
    /// reopening a per-file dialog.
    /// </summary>
    public class MergeConflictSessionViewModel : BaseViewModel
    {
        private readonly Func<string, string> _resolveAbsolutePath;
        private readonly Func<string, Task> _stageFileAsync;
        private readonly Dictionary<string, MergeConflictFileViewModel> _fileVmCache =
            new Dictionary<string, MergeConflictFileViewModel>(StringComparer.OrdinalIgnoreCase);

        public ObservableCollection<ConflictFileListEntry> Files { get; }

        private ConflictFileListEntry _selectedEntry;
        public ConflictFileListEntry SelectedEntry
        {
            get => _selectedEntry;
            set { if (Set(ref _selectedEntry, value)) LoadSelected(); }
        }

        private MergeConflictFileViewModel _currentFile;
        public MergeConflictFileViewModel CurrentFile { get => _currentFile; private set => Set(ref _currentFile, value); }

        public string ResolvedCountLabel => $"{Files.Count(f => f.IsResolved)} of {Files.Count} file(s) resolved";

        public ICommand SaveCurrentCommand { get; }
        public ICommand CloseCommand { get; }

        /// <summary>Per-pane commit info header (GitKraken shows "Commit {sha} on {branch}" above
        /// each side, with a tooltip of author/date/message) — same for every file in the
        /// session, so it's resolved once here rather than re-fetched per file. Null CommitInfo
        /// (e.g. a plain rebase, which has no single tracked "theirs" ref — see
        /// RepositoryViewModel.Rebase.cs.TheirsRefFor) just means no header/tooltip is shown for
        /// that side.</summary>
        public string OursCommitLabel { get; }
        public string OursCommitTooltip { get; }
        public string TheirsCommitLabel { get; }
        public string TheirsCommitTooltip { get; }

        /// <summary>True = at least one file was resolved and staged during this session.</summary>
        public event Action<bool> RequestClose;

        private readonly Dictionary<string, string> _gitAncestorTextByPath;

        public MergeConflictSessionViewModel(IEnumerable<FileChange> conflictedFiles,
            Func<string, string> resolveAbsolutePath, Func<string, Task> stageFileAsync,
            string oursBranch = null, CommitInfo oursCommit = null,
            string theirsDescription = null, CommitInfo theirsCommit = null,
            Dictionary<string, string> gitAncestorTextByPath = null)
        {
            _gitAncestorTextByPath = gitAncestorTextByPath ?? new Dictionary<string, string>();
            _resolveAbsolutePath = resolveAbsolutePath;
            _stageFileAsync = stageFileAsync;
            Files = new ObservableCollection<ConflictFileListEntry>(
                conflictedFiles.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                                .Select(f => new ConflictFileListEntry(f)));

            OursCommitLabel = oursCommit != null ? $"Commit {oursCommit.ShortSha} on {oursBranch}" : oursBranch;
            OursCommitTooltip = FormatCommitTooltip(oursCommit);
            TheirsCommitLabel = theirsCommit != null
                ? $"Commit {theirsCommit.ShortSha}" + (string.IsNullOrEmpty(theirsDescription) ? "" : $" ({theirsDescription})")
                : theirsDescription;
            TheirsCommitTooltip = FormatCommitTooltip(theirsCommit);

            SaveCurrentCommand = new RelayCommand(async () => await SaveCurrentAsync(), () => CurrentFile != null);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke(Files.Any(f => f.IsResolved)));

            SelectedEntry = Files.FirstOrDefault(f => !f.IsResolved) ?? Files.FirstOrDefault();
        }

        private static string FormatCommitTooltip(CommitInfo c) => c == null ? null
            : $"{c.AuthorName} <{c.AuthorEmail}>\n{c.AuthorDate:yyyy-MM-dd HH:mm}\n\n{c.MessageShort}";

        private void LoadSelected()
        {
            var entry = SelectedEntry;
            if (entry == null) { CurrentFile = null; return; }
            if (!_fileVmCache.TryGetValue(entry.Path, out var vm))
            {
                var abs = _resolveAbsolutePath(entry.Path);
                _gitAncestorTextByPath.TryGetValue(entry.Path, out var gitAncestorText);
                vm = new MergeConflictFileViewModel(abs, entry.Path, entry.FileChange.OursMissing, entry.FileChange.TheirsMissing,
                    OursCommitLabel, OursCommitTooltip, TheirsCommitLabel, TheirsCommitTooltip, gitAncestorText);
                _fileVmCache[entry.Path] = vm;
            }
            CurrentFile = vm;
        }

        private async Task SaveCurrentAsync()
        {
            var entry = SelectedEntry;
            var vm = CurrentFile;
            if (entry == null || vm == null) return;

            if (!vm.Save())
            {
                if (vm.IsExistenceConflict)
                {
                    DialogService.ShowError("Cannot Save", "Choose Keep File or Delete File before saving.");
                    return;
                }
                if (!DialogService.Confirm("Save with Unresolved Conflicts",
                        $"{vm.UnresolvedCount} conflict(s) still remain in {entry.Path}. Save anyway?",
                        "Save Anyway", danger: true))
                    return;
                if (!vm.Save(force: true)) return;
            }

            entry.IsResolved = true;
            RaisePropertyChanged(nameof(ResolvedCountLabel));
            await _stageFileAsync(entry.Path);

            SelectedEntry = Files.FirstOrDefault(f => !f.IsResolved);
        }
    }
}
