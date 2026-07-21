using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using PickleGit.Models;
using PickleGit.Services;

namespace PickleGit.ViewModels
{
    public enum ExistenceChoice { Undecided, KeepFile, DeleteFile }

    /// <summary>One row in a content-conflict file's flattened, GitKraken-style merged view —
    /// either a run of already-agreed context text, or one still-interactive conflict block.
    /// Wraps a ConflictDocItem with the per-block commands the view binds directly to (no "current
    /// block" navigation required — every block can be resolved from its own inline buttons).</summary>
    public class ConflictViewItem
    {
        public ConflictDocItemKind Kind { get; }
        public string ContextText { get; }
        public MergeConflictBlock Block { get; }

        public ICommand AcceptMineCommand { get; }
        public ICommand AcceptTheirsCommand { get; }
        public ICommand AcceptBothCommand { get; }
        public ICommand AcceptBothReverseCommand { get; }
        public ICommand AcceptBaseCommand { get; }

        /// <summary>Set once Block.Resolution != Unresolved — the text that was substituted in,
        /// for display in the collapsed "resolved" row. A fresh ConflictViewItem is built for
        /// every block on every resolve (see MergeConflictFileViewModel.RebuildFlatItems), so this
        /// is safe to compute once at construction rather than needing live change notification.</summary>
        public string ResolvedText { get; }
        public string ResolvedLabel { get; }

        public ConflictViewItem(ConflictDocItem item, MergeConflictFileViewModel owner)
        {
            Kind = item.Kind;
            ContextText = item.ContextText;
            Block = item.Block;
            if (Kind != ConflictDocItemKind.Block) return;

            AcceptMineCommand = new RelayCommand(() => owner.Resolve(Block, ConflictResolution.Ours));
            AcceptTheirsCommand = new RelayCommand(() => owner.Resolve(Block, ConflictResolution.Theirs));
            AcceptBothCommand = new RelayCommand(() => owner.Resolve(Block, ConflictResolution.Both));
            AcceptBothReverseCommand = new RelayCommand(() => owner.Resolve(Block, ConflictResolution.BothReverse));
            AcceptBaseCommand = new RelayCommand(() => owner.Resolve(Block, ConflictResolution.Base),
                () => Block.BaseText != null);

            switch (Block.Resolution)
            {
                case ConflictResolution.Ours:
                    ResolvedText = Block.OursText; ResolvedLabel = "Resolved — kept mine"; break;
                case ConflictResolution.Theirs:
                    ResolvedText = Block.TheirsText; ResolvedLabel = "Resolved — kept theirs"; break;
                case ConflictResolution.Both:
                    ResolvedText = string.Join("\n", new[] { Block.OursText, Block.TheirsText }.Where(s => s.Length > 0));
                    ResolvedLabel = "Resolved — kept both (mine, then theirs)"; break;
                case ConflictResolution.BothReverse:
                    ResolvedText = string.Join("\n", new[] { Block.TheirsText, Block.OursText }.Where(s => s.Length > 0));
                    ResolvedLabel = "Resolved — kept both (theirs, then mine)"; break;
                case ConflictResolution.Base:
                    ResolvedText = Block.BaseText ?? string.Empty; ResolvedLabel = "Resolved — kept base"; break;
            }
        }
    }

    /// <summary>
    /// One conflicted file's merge state. For a content conflict, parses marker blocks and
    /// exposes a flattened, interleaved view (FlatItems) that renders non-conflicting text
    /// normally and each conflict block inline with its own Accept Mine/Theirs/Both buttons —
    /// resolving a block edits the same authoritative ResultText the original single-pane editor
    /// used, just presented as one merged document instead of separate Ours/Theirs/Result panes.
    /// For an add/delete existence conflict (no marker content at all — see FileChange.OursMissing/
    /// TheirsMissing), there is no document to parse; the file is resolved by an explicit
    /// Keep/Delete choice instead.
    /// </summary>
    public class MergeConflictFileViewModel : BaseViewModel
    {
        private readonly string _absolutePath;
        private readonly MergeConflictDocument _doc;
        private readonly System.Text.Encoding _encoding;

        public string RelativePath { get; }
        public bool IsExistenceConflict { get; }
        public bool OursMissing { get; }
        public bool TheirsMissing { get; }

        /// <summary>Human-readable reason shown in the existence-conflict banner.</summary>
        public string ExistenceMessage => OursMissing
            ? "This file does not exist on your side of the merge (deleted, or added only on theirs)."
            : "This file does not exist on their side of the merge (deleted, or added only on yours).";

        private string _resultText;
        /// <summary>The authoritative editable buffer — starts as the raw conflicted file content
        /// and has each resolved block's marker span replaced in place. Not shown directly; kept
        /// as the source of truth that FlatItems is rendered from and Save() writes to disk.</summary>
        public string ResultText
        {
            get => _resultText;
            private set { if (Set(ref _resultText, value)) RaisePropertyChanged(nameof(UnresolvedCount)); }
        }

        public int UnresolvedCount => _doc == null ? 0 : CountMarkers(ResultText);

        public string BlockStatusLabel => _doc == null ? null
            : $"{_doc.Blocks.Count(b => b.Resolution != ConflictResolution.Unresolved)} of {_doc.Blocks.Count} conflict(s) resolved";

        private ObservableCollection<ConflictViewItem> _flatItems = new ObservableCollection<ConflictViewItem>();
        public ObservableCollection<ConflictViewItem> FlatItems
        {
            get => _flatItems;
            private set => Set(ref _flatItems, value);
        }

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

        public MergeConflictFileViewModel(string absolutePath, string relativePath, bool oursMissing, bool theirsMissing)
        {
            _absolutePath = absolutePath;
            RelativePath = relativePath;
            OursMissing = oursMissing;
            TheirsMissing = theirsMissing;
            IsExistenceConflict = oursMissing || theirsMissing;

            KeepFileCommand = new RelayCommand(() => ExistenceChoice = ExistenceChoice.KeepFile);
            DeleteFileCommand = new RelayCommand(() => ExistenceChoice = ExistenceChoice.DeleteFile);

            if (IsExistenceConflict) return; // nothing to parse — resolved by keep/delete choice alone

            if (!File.Exists(absolutePath))
            {
                // Defensive: status reported a content conflict, but the file is gone from disk
                // (e.g. deleted externally between the status read and opening this editor) —
                // there's nothing to show or resolve here rather than throwing.
                _doc = new MergeConflictDocument();
                _resultText = string.Empty;
                RebuildFlatItems();
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
            _resultText = content;
            RebuildFlatItems();
        }

        private void RebuildFlatItems()
        {
            FlatItems = new ObservableCollection<ConflictViewItem>(
                _doc.Items.Select(i => new ConflictViewItem(i, this)));
        }

        /// <summary>Applies a resolution to one block, in place, regardless of whether it's the
        /// "current" one — every block's inline buttons call this directly.</summary>
        public void Resolve(MergeConflictBlock block, ConflictResolution resolution)
        {
            if (block == null || block.Resolution != ConflictResolution.Unresolved) return;

            string replacement;
            switch (resolution)
            {
                case ConflictResolution.Ours: replacement = block.OursText; break;
                case ConflictResolution.Theirs: replacement = block.TheirsText; break;
                case ConflictResolution.Both:
                    replacement = string.Join("\n", new[] { block.OursText, block.TheirsText }.Where(s => s.Length > 0));
                    break;
                case ConflictResolution.BothReverse:
                    replacement = string.Join("\n", new[] { block.TheirsText, block.OursText }.Where(s => s.Length > 0));
                    break;
                case ConflictResolution.Base: replacement = block.BaseText ?? string.Empty; break;
                default: return;
            }

            // Identical blocks can occur more than once; resolved ones are already gone from
            // ResultText, so this block maps to the Nth remaining occurrence where N is the
            // number of earlier still-unresolved blocks with the same raw text.
            int occurrence = _doc.Blocks.Count(b => b.Index < block.Index
                && b.Resolution == ConflictResolution.Unresolved
                && b.RawText == block.RawText);
            var idx = -1;
            for (int i = 0; i <= occurrence; i++)
            {
                idx = ResultText.IndexOf(block.RawText, idx + 1, StringComparison.Ordinal);
                if (idx < 0) return; // the marker region was hand-edited — leave it alone
            }
            ResultText = ResultText.Substring(0, idx) + replacement + ResultText.Substring(idx + block.RawText.Length);
            block.Resolution = resolution;
            RaisePropertyChanged(nameof(IsResolved));
            RaisePropertyChanged(nameof(BlockStatusLabel));
            RebuildFlatItems();
        }

        /// <summary>Writes the resolution to disk (content conflict) or performs the keep/delete
        /// choice on the working-tree file (existence conflict). Returns false without writing
        /// anything when the file isn't actually resolved yet, unless <paramref name="force"/> is
        /// set (used to save a content conflict that still has marker blocks left, after the
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

        private static int CountMarkers(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int count = 0, idx = 0;
            while ((idx = text.IndexOf("<<<<<<< ", idx, StringComparison.Ordinal)) >= 0) { count++; idx += 8; }
            return count;
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

        /// <summary>True = at least one file was resolved and staged during this session.</summary>
        public event Action<bool> RequestClose;

        public MergeConflictSessionViewModel(IEnumerable<FileChange> conflictedFiles,
            Func<string, string> resolveAbsolutePath, Func<string, Task> stageFileAsync)
        {
            _resolveAbsolutePath = resolveAbsolutePath;
            _stageFileAsync = stageFileAsync;
            Files = new ObservableCollection<ConflictFileListEntry>(
                conflictedFiles.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                                .Select(f => new ConflictFileListEntry(f)));

            SaveCurrentCommand = new RelayCommand(async () => await SaveCurrentAsync(), () => CurrentFile != null);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke(Files.Any(f => f.IsResolved)));

            SelectedEntry = Files.FirstOrDefault(f => !f.IsResolved) ?? Files.FirstOrDefault();
        }

        private void LoadSelected()
        {
            var entry = SelectedEntry;
            if (entry == null) { CurrentFile = null; return; }
            if (!_fileVmCache.TryGetValue(entry.Path, out var vm))
            {
                var abs = _resolveAbsolutePath(entry.Path);
                vm = new MergeConflictFileViewModel(abs, entry.Path, entry.FileChange.OursMissing, entry.FileChange.TheirsMissing);
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
                        $"{vm.UnresolvedCount} conflict marker block(s) still remain in {entry.Path}. Save anyway?",
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
