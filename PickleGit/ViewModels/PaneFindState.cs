using System;
using System.Collections.Generic;
using System.Windows.Input;
using PickleGit.Models;

namespace PickleGit.ViewModels
{
    /// <summary>One pane's independent "Find" state — its own open/closed flag, search text,
    /// match list, current-match pointer, and Next/Prev navigation. Used so DiffView's
    /// Unified/SideBySide-Left/SideBySide-Right panes and the merge conflict editor's
    /// Left/Right/Result panes each get their own scoped Find instead of one shared search
    /// spanning (and highlighting every occurrence across) all of them at once.
    ///
    /// The owning view model supplies <paramref name="computeMatches"/> — how to turn a search
    /// term into an ordered list of this pane's own (row, character-offset, length) occurrences.
    /// Each occurrence is its own distinct match: a row where the term appears twice contributes
    /// two entries, not one, so Next/Prev steps through every occurrence individually and only
    /// the one currently selected gets highlighted (see CurrentMatchRange) — previously the whole
    /// row was treated as a single match and every occurrence in it lit up together, which was
    /// wrong for a row containing more than one hit. This class only owns the open/text/position/
    /// status bookkeeping and the wraparound navigation (via <see cref="FindNavigationHelper"/>),
    /// not the pane-specific data access.</summary>
    public sealed class PaneFindState : BaseViewModel
    {
        private readonly Func<string, List<(object Item, int Start, int Length)>> _computeMatches;

        public PaneFindState(Func<string, List<(object Item, int Start, int Length)>> computeMatches)
        {
            _computeMatches = computeMatches;
            NextCommand = new RelayCommand(() => Navigate(+1));
            PrevCommand = new RelayCommand(() => Navigate(-1));
        }

        private bool _isOpen;
        public bool IsOpen
        {
            get => _isOpen;
            set { if (Set(ref _isOpen, value) && !value) SearchText = string.Empty; }
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { if (Set(ref _searchText, value)) Recompute(); }
        }

        private string _status;
        public string Status { get => _status; private set => Set(ref _status, value); }

        /// <summary>The one row object that should show the search highlight right now — null
        /// when the pane's Find is closed, empty, or has zero matches. Compared by reference
        /// against each row's own DataContext (see Converters/ValueConverters.cs's
        /// CurrentFindMatchConverter) to decide whether that single row is even a candidate for
        /// highlighting; CurrentMatchRange then pins down exactly which occurrence within it.</summary>
        private object _currentMatch;
        public object CurrentMatch { get => _currentMatch; private set => Set(ref _currentMatch, value); }

        /// <summary>The exact (start, length) span within CurrentMatch's row that is the current
        /// find result — null on no active match. A row containing the term more than once relies
        /// on this to highlight only the one occurrence actually being navigated to.</summary>
        private DiffHighlightSpan? _currentMatchRange;
        public DiffHighlightSpan? CurrentMatchRange { get => _currentMatchRange; private set => Set(ref _currentMatchRange, value); }

        public ICommand NextCommand { get; }
        public ICommand PrevCommand { get; }

        /// <summary>Fired with the row object to scroll into view once navigation lands on it.</summary>
        public event Action<object> ScrollToMatchRequested;

        private List<(object Item, int Start, int Length)> _matches = new List<(object, int, int)>();
        private int _pos = -1;

        public void Recompute()
        {
            _matches = string.IsNullOrEmpty(_searchText)
                ? new List<(object, int, int)>()
                : (_computeMatches(_searchText) ?? new List<(object, int, int)>());
            _pos = -1;
            CurrentMatch = null;
            CurrentMatchRange = null;
            Status = FindNavigationHelper.MatchCountStatus(_searchText, _matches.Count);
            if (_matches.Count > 0) Navigate(+1);
        }

        private void Navigate(int direction)
        {
            if (_matches.Count == 0) return;
            _pos = FindNavigationHelper.Advance(_pos, direction, _matches.Count);
            Status = FindNavigationHelper.PositionStatus(_pos, _matches.Count);
            var (item, start, length) = _matches[_pos];
            CurrentMatch = item;
            CurrentMatchRange = new DiffHighlightSpan(start, length);
            ScrollToMatchRequested?.Invoke(CurrentMatch);
        }

        /// <summary>Call whenever this pane's underlying row list is rebuilt/replaced (file switch,
        /// reload, a toggle that regenerates rows) — stale row references in the match list would
        /// otherwise no longer belong to the live ItemsSource. Recomputes against the fresh list
        /// rather than closing outright, so an active search stays open across the reload.</summary>
        public void Invalidate() => Recompute();
    }
}
