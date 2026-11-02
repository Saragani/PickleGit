using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace PickleGit.ViewModels
{
    /// <summary>A collection whose change notifications can be paused while many items change.</summary>
    public interface ISuspendableNotifications
    {
        /// <summary>Pauses CollectionChanged/PropertyChanged until the returned scope is disposed;
        /// then raises a single Reset if anything changed. Scopes nest.</summary>
        IDisposable SuspendNotifications();
    }

    /// <summary>
    /// ObservableCollection that can apply many changes as one notification. Used for the file/tree
    /// selection collections: a Shift-range or a whole-folder selection otherwise raised one
    /// CollectionChanged per item, and each one re-synced every bound ListView's selection (O(n²) for
    /// a few thousand files). ListViewMultiSelectBehavior suspends it around a selection burst.
    /// </summary>
    public class BulkObservableCollection<T> : ObservableCollection<T>, ISuspendableNotifications
    {
        private int _suspendDepth;
        private bool _changedWhileSuspended;

        /// <summary>Raised after a change notification has been fully delivered — outside
        /// ObservableCollection's reentrancy guard, so a handler may modify this collection again (a
        /// CollectionChanged handler can't: with more than one subscriber, WPF throws
        /// "Cannot change ObservableCollection during a CollectionChanged event").</summary>
        public event EventHandler ChangesApplied;

        public IDisposable SuspendNotifications()
        {
            _suspendDepth++;
            return new Scope(this);
        }

        /// <summary>Replaces the contents with <paramref name="items"/> as a single Reset notification.</summary>
        public void ReplaceAll(IEnumerable<T> items)
        {
            using (SuspendNotifications())
            {
                Clear();
                foreach (var item in items) Add(item);
            }
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (_suspendDepth > 0) { _changedWhileSuspended = true; return; }
            base.OnCollectionChanged(e);
            ChangesApplied?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (_suspendDepth > 0) return;
            base.OnPropertyChanged(e);
        }

        private void Resume()
        {
            if (--_suspendDepth > 0 || !_changedWhileSuspended) return;
            _changedWhileSuspended = false;
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            base.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            base.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            ChangesApplied?.Invoke(this, EventArgs.Empty);
        }

        private sealed class Scope : IDisposable
        {
            private BulkObservableCollection<T> _owner;
            public Scope(BulkObservableCollection<T> owner) { _owner = owner; }
            public void Dispose()
            {
                _owner?.Resume();
                _owner = null;
            }
        }
    }
}
