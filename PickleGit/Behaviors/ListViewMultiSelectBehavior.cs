using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Xaml.Behaviors;

namespace PickleGit.Behaviors
{
    // Bidirectional sync between ListView.SelectedItems (non-bindable IList) and a
    // ViewModel ObservableCollection<T>.  User gestures push into the collection;
    // programmatic changes to the collection sync back to the ListView selection.
    public class ListViewMultiSelectBehavior : Behavior<ListView>
    {
        private bool _updating;

        public static readonly DependencyProperty SelectedItemsProperty =
            DependencyProperty.Register(
                nameof(SelectedItems),
                typeof(IList),
                typeof(ListViewMultiSelectBehavior),
                new PropertyMetadata(null, OnSelectedItemsPropertyChanged));

        public IList SelectedItems
        {
            get => (IList)GetValue(SelectedItemsProperty);
            set => SetValue(SelectedItemsProperty, value);
        }

        // Called when the bound collection instance is replaced (e.g. tab switch).
        private static void OnSelectedItemsPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is ListViewMultiSelectBehavior b)) return;

            if (e.OldValue is INotifyCollectionChanged old)
                old.CollectionChanged -= b.OnViewModelCollectionChanged;

            if (e.NewValue is INotifyCollectionChanged next)
                next.CollectionChanged += b.OnViewModelCollectionChanged;

            b.SyncListViewFromViewModel();
        }

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.SelectionChanged += OnListViewSelectionChanged;

            if (SelectedItems is INotifyCollectionChanged obs)
                obs.CollectionChanged += OnViewModelCollectionChanged;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.SelectionChanged -= OnListViewSelectionChanged;

            if (SelectedItems is INotifyCollectionChanged obs)
                obs.CollectionChanged -= OnViewModelCollectionChanged;

            base.OnDetaching();
        }

        // User clicked in the ListView → push changes into the ViewModel collection.
        //
        // The TabControl hides an inactive tab's content rather than tearing it out of the visual
        // tree: switching away from a tab with a selected commit fires a SelectionChanged clearing
        // it (removed=N, added=0). Checking AssociatedObject.IsVisible synchronously at that point
        // is NOT reliable — confirmed via direct instrumentation (AppLog) that it can still read
        // True at the exact moment this fires (timing depends on how much layout work the newly
        // active tab's content needs; a heavier destination tab can leave the old tab's IsVisible
        // flip until later). Without a guard, this WPF-internal clear mirrors straight into the
        // persisted ViewModel collection, permanently wiping the selection a tab switch is supposed
        // to preserve (SelectedNode/SelectedNodes are otherwise plain VM state that survives
        // switching tabs untouched).
        //
        // Fix: a "cleared to nothing, nothing added" change is the only ambiguous shape (a genuine
        // partial selection change, e.g. picking a different row, always has AddedItems.Count > 0
        // and is relayed immediately below). For that one ambiguous shape, defer the relay one
        // dispatcher pass and re-check IsVisible then — by Background priority the layout pass that
        // actually hides the old tab's content has had time to run, so a real tab-switch clear now
        // reads False while a genuine user deselect (Ctrl+click, clicking empty space) still reads
        // True. The one-tick delay on a real deselect is imperceptible (there's nothing to load for
        // an empty selection anyway).
        private void OnListViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating || SelectedItems == null) return;

            if (e.AddedItems.Count == 0 && e.RemovedItems.Count > 0)
            {
                var removed = new ArrayList(e.RemovedItems);
                var lv = AssociatedObject;
                lv.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    if (_updating || SelectedItems == null || lv.IsVisible == false) return;
                    _updating = true;
                    try
                    {
                        // IsVisible alone isn't enough: a shrink-then-grow drag (select 1-10, shrink
                        // to 1-5 — queuing this deferred removal of 6-10 — then drag back past 10
                        // before mouse-up) re-adds the captured items to the ListView's own live
                        // selection before this callback fires, with IsVisible staying true the whole
                        // time (no tab switch occurred). Removing them from SelectedItems here anyway
                        // would silently desync the VM collection from what's actually still
                        // highlighted. Re-check against the ListView's own current SelectedItems —
                        // the real-time source of truth — and only drop an item that's genuinely
                        // still deselected there.
                        using (Suspend())
                        foreach (var item in removed)
                            if (!lv.SelectedItems.Contains(item)) SelectedItems.Remove(item);
                    }
                    finally { _updating = false; }
                    ResyncIfViewModelAdjusted();
                }), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            _updating = true;
            try
            {
                // One selection gesture (Shift-range, Ctrl+A, a folder) can add thousands of items:
                // apply them as a single notification when the collection supports it.
                using (Suspend())
                {
                    foreach (var item in e.RemovedItems) SelectedItems.Remove(item);
                    foreach (var item in e.AddedItems)
                        if (!SelectedItems.Contains(item)) SelectedItems.Add(item);
                }
            }
            finally { _updating = false; }
            ResyncIfViewModelAdjusted();
        }

        /// <summary>The ViewModel may react to a selection change by adjusting the collection itself
        /// (e.g. the staging tree replacing a selected folder with its remaining files when one of its
        /// files is deselected). That happens while _updating is set, so OnViewModelCollectionChanged
        /// ignored it — push the adjusted collection back to the ListView if the two now differ.</summary>
        private void ResyncIfViewModelAdjusted()
        {
            var lv = AssociatedObject;
            if (lv == null || SelectedItems == null) return;
            bool same = lv.SelectedItems.Count == SelectedItems.Count;
            if (same)
                foreach (var item in SelectedItems)
                    if (!lv.SelectedItems.Contains(item)) { same = false; break; }
            if (!same) SyncListViewFromViewModel();
        }

        /// <summary>Pauses the bound collection's notifications for a batch (one Reset at the end),
        /// when it supports that; a no-op scope otherwise.</summary>
        private System.IDisposable Suspend() =>
            (SelectedItems as PickleGit.ViewModels.ISuspendableNotifications)?.SuspendNotifications() ?? NoopScope.Instance;

        private sealed class NoopScope : System.IDisposable
        {
            public static readonly NoopScope Instance = new NoopScope();
            public void Dispose() { }
        }

        // ViewModel collection changed from code → push changes into ListView.
        private void OnViewModelCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_updating) return;
            SyncListViewFromViewModel();
        }

        private void SyncListViewFromViewModel()
        {
            if (_updating || AssociatedObject == null || SelectedItems == null) return;
            _updating = true;
            try
            {
                AssociatedObject.SelectedItems.Clear();
                foreach (var item in SelectedItems)
                    AssociatedObject.SelectedItems.Add(item);
            }
            finally { _updating = false; }
        }
    }
}
