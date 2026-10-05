using System;
using System.Collections;
using System.Collections.Generic;

namespace MyExtension.Package
{
    /// <summary>
    /// A9: bounded, O(1) move-to-front MRU of file paths — the session-MRU floor behind
    /// <see cref="RecentFilesGatherer"/>. <see cref="Add"/> removes an existing path from its
    /// current position (O(1) via the linked list + index) and inserts it at the front; when the
    /// list is at capacity the least-recent entry is evicted. Dependency-free so it is
    /// unit-testable.
    /// </summary>
    internal sealed class RecentFilesMru : IEnumerable<string>
    {
        private readonly int _capacity;
        private readonly LinkedList<string> _list = new LinkedList<string>();
        private readonly Dictionary<string, LinkedListNode<string>> _index =
            new Dictionary<string, LinkedListNode<string>>(StringComparer.Ordinal);

        public RecentFilesMru(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            _capacity = capacity;
        }

        public int Count => _list.Count;

        public string? MostRecent => _list.First?.Value;

        public void Add(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            if (_index.TryGetValue(path, out var node))
            {
                _list.Remove(node);
                _index.Remove(path);
            }
            _list.AddFirst(path);
            _index[path] = _list.First!;
            while (_list.Count > _capacity)
            {
                var last = _list.Last!;
                _list.RemoveLast();
                _index.Remove(last.Value);
            }
        }

        public List<string> ToList()
        {
            var result = new List<string>(_list.Count);
            for (var node = _list.First; node != null; node = node.Next)
            {
                result.Add(node.Value);
            }
            return result;
        }

        public IEnumerator<string> GetEnumerator() => ToList().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
