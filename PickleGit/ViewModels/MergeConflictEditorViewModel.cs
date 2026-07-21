using System;
using System.IO;
using System.Linq;
using System.Windows.Input;
using PickleGit.Models;
using PickleGit.Services;

namespace PickleGit.ViewModels
{
    /// <summary>
    /// Backs Views/MergeConflictEditorWindow.xaml — a 3-pane conflict resolver for one
    /// working-tree file. OURS/THEIRS are read-only full-file reconstructions; RESULT is
    /// the live editable text (starts as the raw conflicted file). Per-block buttons
    /// replace that block's marker span in RESULT; Save writes the file back to disk
    /// (the caller is responsible for `git add`-ing it afterwards).
    /// </summary>
    public class MergeConflictEditorViewModel : BaseViewModel
    {
        private readonly string _absolutePath;
        private readonly MergeConflictDocument _doc;

        public string FilePath { get; }
        public string OursText { get; }
        public string TheirsText { get; }
        public bool HasConflicts => _doc.Blocks.Count > 0;

        private string _resultText;
        public string ResultText
        {
            get => _resultText;
            set { if (Set(ref _resultText, value)) RaisePropertyChanged(nameof(UnresolvedCount)); }
        }

        private int _currentBlockIndex;
        public int CurrentBlockIndex
        {
            get => _currentBlockIndex;
            set
            {
                var clamped = _doc.Blocks.Count == 0 ? 0 : Math.Max(0, Math.Min(value, _doc.Blocks.Count - 1));
                if (Set(ref _currentBlockIndex, clamped))
                    RaisePropertyChanged(nameof(CurrentBlock));
                RaisePropertyChanged(nameof(BlockPositionLabel));
            }
        }

        public MergeConflictBlock CurrentBlock => _doc.Blocks.Count == 0 ? null : _doc.Blocks[_currentBlockIndex];

        public string BlockPositionLabel => _doc.Blocks.Count == 0
            ? "No conflicts remaining"
            : $"Block {CurrentBlockIndex + 1} of {_doc.Blocks.Count}";

        public int UnresolvedCount => CountMarkers(ResultText);

        public ICommand PrevBlockCommand { get; }
        public ICommand NextBlockCommand { get; }
        public ICommand TakeOursCommand { get; }
        public ICommand TakeTheirsCommand { get; }
        public ICommand TakeBothCommand { get; }
        public ICommand TakeBothReverseCommand { get; }
        public ICommand TakeBaseCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        /// <summary>True = saved, false/null = cancelled.</summary>
        public event Action<bool> RequestClose;

        private readonly System.Text.Encoding _encoding;

        public MergeConflictEditorViewModel(string absolutePath, string relativePath)
        {
            _absolutePath = absolutePath;
            FilePath = relativePath;
            // Preserve the file's encoding: honor a BOM if present, otherwise read and
            // write back as BOM-less UTF-8 (never introduce a BOM the file didn't have).
            string content;
            using (var reader = new StreamReader(absolutePath,
                new System.Text.UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
            {
                content = reader.ReadToEnd();
                _encoding = reader.CurrentEncoding;
            }
            _doc = MergeConflictParser.Parse(content);
            OursText = _doc.OursText;
            TheirsText = _doc.TheirsText;
            _resultText = content;

            PrevBlockCommand = new RelayCommand(() => CurrentBlockIndex--, () => CurrentBlockIndex > 0);
            NextBlockCommand = new RelayCommand(() => CurrentBlockIndex++, () => CurrentBlockIndex < _doc.Blocks.Count - 1);
            TakeOursCommand = new RelayCommand(() => ResolveCurrent(ConflictResolution.Ours), () => CurrentBlock != null);
            TakeTheirsCommand = new RelayCommand(() => ResolveCurrent(ConflictResolution.Theirs), () => CurrentBlock != null);
            TakeBothCommand = new RelayCommand(() => ResolveCurrent(ConflictResolution.Both), () => CurrentBlock != null);
            TakeBothReverseCommand = new RelayCommand(() => ResolveCurrent(ConflictResolution.BothReverse), () => CurrentBlock != null);
            TakeBaseCommand = new RelayCommand(() => ResolveCurrent(ConflictResolution.Base),
                () => CurrentBlock?.BaseText != null);
            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        private void ResolveCurrent(ConflictResolution resolution)
        {
            var block = CurrentBlock;
            if (block == null || block.Resolution != ConflictResolution.Unresolved) return;

            string replacement;
            switch (resolution)
            {
                case ConflictResolution.Ours: replacement = block.OursText; break;
                case ConflictResolution.Theirs: replacement = block.TheirsText; break;
                case ConflictResolution.Both: replacement = string.Join("\n", new[] { block.OursText, block.TheirsText }.Where(s => s.Length > 0)); break;
                case ConflictResolution.BothReverse: replacement = string.Join("\n", new[] { block.TheirsText, block.OursText }.Where(s => s.Length > 0)); break;
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
            RaisePropertyChanged(nameof(CurrentBlock));

            var next = _doc.Blocks.Skip(CurrentBlockIndex + 1).FirstOrDefault(b => b.Resolution == ConflictResolution.Unresolved);
            if (next != null) CurrentBlockIndex = next.Index;
        }

        private void Save()
        {
            if (UnresolvedCount > 0 && !DialogService.Confirm("Save with Unresolved Conflicts",
                    $"{UnresolvedCount} conflict marker block(s) still remain in the file. Save anyway?",
                    "Save Anyway", danger: true))
                return;
            File.WriteAllText(_absolutePath, ResultText, _encoding);
            RequestClose?.Invoke(true);
        }

        private static int CountMarkers(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int count = 0, idx = 0;
            while ((idx = text.IndexOf("<<<<<<< ", idx, StringComparison.Ordinal)) >= 0) { count++; idx += 8; }
            return count;
        }
    }
}
