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
        private void OnListViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating || SelectedItems == null) return;
            _updating = true;
            try
            {
                foreach (var item in e.RemovedItems) SelectedItems.Remove(item);
                foreach (var item in e.AddedItems)
                    if (!SelectedItems.Contains(item)) SelectedItems.Add(item);
            }
            finally { _updating = false; }
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
