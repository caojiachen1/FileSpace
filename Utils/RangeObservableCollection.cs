using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace FileSpace.Utils
{
    /// <summary>
    /// 高性能 ObservableCollection，支持批量操作以减少 UI 通知次数。
    /// 在需要添加/删除大量项目时，使用 AddRange/RemoveRange/ReplaceAll 方法可以显著提升性能。
    /// </summary>
    public class RangeObservableCollection<T> : ObservableCollection<T>
    {
        private bool _suppressNotification;
        private int _bulkOperationCount;

        public RangeObservableCollection() : base() { }

        public RangeObservableCollection(IEnumerable<T> collection) : base(collection) { }

        /// <summary>
        /// 批量添加项目，只触发一次 CollectionChanged 通知
        /// </summary>
        public void AddRange(IEnumerable<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            var list = items as IList<T> ?? new List<T>(items);
            if (list.Count == 0) return;

            CheckReentrancy();
            
            _suppressNotification = true;
            try
            {
                foreach (var item in list)
                {
                    Items.Add(item);
                }
            }
            finally
            {
                _suppressNotification = false;
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        /// <summary>
        /// 批量移除项目，只触发一次 CollectionChanged 通知
        /// </summary>
        public void RemoveRange(IEnumerable<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            CheckReentrancy();
            
            _suppressNotification = true;
            try
            {
                foreach (var item in items)
                {
                    Items.Remove(item);
                }
            }
            finally
            {
                _suppressNotification = false;
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        /// <summary>
        /// 用新集合完全替换当前集合，只触发一次 CollectionChanged 通知。
        /// 这是加载大量数据时最高效的方法。
        /// </summary>
        public void ReplaceAll(IEnumerable<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            CheckReentrancy();
            
            _suppressNotification = true;
            try
            {
                Items.Clear();
                
                // 如果底层 List 支持容量设置，预分配容量
                if (items is ICollection<T> collection && Items is List<T> list)
                {
                    list.Capacity = Math.Max(list.Capacity, collection.Count);
                }
                
                foreach (var item in items)
                {
                    Items.Add(item);
                }
            }
            finally
            {
                _suppressNotification = false;
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        /// <summary>
        /// 清空集合并用新集合替换，使用预分配容量以减少内存分配
        /// </summary>
        public void ReplaceAll(IList<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            CheckReentrancy();
            
            _suppressNotification = true;
            try
            {
                Items.Clear();
                
                // 如果底层 List 支持容量设置，预分配容量
                if (Items is List<T> list)
                {
                    list.Capacity = Math.Max(list.Capacity, items.Count);
                }

                foreach (var item in items)
                {
                    Items.Add(item);
                }
            }
            finally
            {
                _suppressNotification = false;
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        /// <summary>
        /// 开始批量操作，暂时禁止 CollectionChanged 通知
        /// 支持嵌套调用
        /// </summary>
        public IDisposable BeginBulkOperation()
        {
            return new BulkOperationScope(this);
        }

        /// <summary>
        /// 在禁止通知的情况下执行操作
        /// </summary>
        public void SuppressedAction(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            
            _suppressNotification = true;
            try
            {
                action();
            }
            finally
            {
                _suppressNotification = false;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
                OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!_suppressNotification)
            {
                base.OnCollectionChanged(e);
            }
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (!_suppressNotification)
            {
                base.OnPropertyChanged(e);
            }
        }

        private class BulkOperationScope : IDisposable
        {
            private readonly RangeObservableCollection<T> _collection;
            private bool _disposed;

            public BulkOperationScope(RangeObservableCollection<T> collection)
            {
                _collection = collection;
                Interlocked.Increment(ref _collection._bulkOperationCount);
                _collection._suppressNotification = true;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                
                var count = Interlocked.Decrement(ref _collection._bulkOperationCount);
                if (count == 0)
                {
                    _collection._suppressNotification = false;
                    _collection.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
                    _collection.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
                    _collection.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                }
            }
        }
    }
}
