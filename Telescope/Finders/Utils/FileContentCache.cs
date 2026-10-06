using System;
using System.Collections.Generic;
using System.IO;

namespace Telescope.Finders
{
    /// <summary>
    /// Caches per-file line content keyed by <c>LastWriteTimeUtc</c>, so a finder's per-file scan
    /// stops re-reading every file from disk on each query. Injected timestamp/reader delegates keep
    /// the unit tests hermetic (no filesystem-timestamp flakiness).
    /// </summary>
    internal sealed class FileContentCache
    {
        private readonly Func<string, DateTime> _timestamp;
        private readonly Func<string, string[]> _reader;
        private readonly Func<string, string> _contentReader;
        private readonly int? _maxEntries;

        private readonly Dictionary<string, CacheEntry> _entries =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        // m9 (BP-15): O(1) LinkedList+Dictionary LRU — most-recently-used at the head. On a hit the
        // key moves to the head; on an insert past the cap the tail node + its dictionary entry are
        // removed. GetContent shares _entries, so the LRU covers BOTH GetLines and GetContent (the
        // exact-total invariant count <= cap is preserved).
        private readonly LinkedList<string> _lru = new LinkedList<string>();

        // m10 (BP-1): the cache is shared by the UI thread (the finder gather) and the off-thread
        // Grep scan loop (BP-2/M1), so every mutation is serialized under ONE gate. The injected
        // timestamp/reader/contentReader delegates run INSIDE the lock (they are the file I/O the
        // lock protects). EvictIfNeeded is private and called from inside the same lock — no nested
        // locks, no re-entrancy.
        private readonly object _gate = new object();

        /// <param name="maxEntries">
        /// Optional LRU cap (M13): when set, inserting beyond this many entries evicts the
        /// least-recently-used entry.
        /// </param>
        public FileContentCache(int? maxEntries = null, Func<string, DateTime>? timestamp = null, Func<string, string[]>? reader = null, Func<string, string>? contentReader = null)
        {
            _maxEntries = maxEntries;
            _timestamp = timestamp ?? File.GetLastWriteTimeUtc;
            _reader = reader ?? File.ReadAllLines;
            _contentReader = contentReader ?? File.ReadAllText;
        }

        /// <summary>
        /// Returns the cached lines when the file's <c>LastWriteTimeUtc</c> is unchanged, else
        /// re-reads and caches them.
        /// </summary>
        public string[] GetLines(string path)
        {
            lock (_gate)
            {
                DateTime stamp = _timestamp(path);
                if (_entries.TryGetValue(path, out CacheEntry entry) && entry.Timestamp == stamp && entry.Lines != null)
                {
                    Touch(entry);
                    return entry.Lines;
                }
                string[] lines = _reader(path);
                Put(path, stamp, lines, null);
                return lines;
            }
        }

        /// <summary>
        /// Returns the cached full file content when the file's <c>LastWriteTimeUtc</c> is
        /// unchanged, else re-reads and caches it (M5 — the preview re-reads/re-tokenizes only on
        /// change). The exact content string is preserved (no line-ending normalization).
        /// </summary>
        public string GetContent(string path)
        {
            lock (_gate)
            {
                DateTime stamp = _timestamp(path);
                if (_entries.TryGetValue(path, out CacheEntry entry) && entry.Timestamp == stamp && entry.Content != null)
                {
                    Touch(entry);
                    return entry.Content;
                }
                string content = _contentReader(path);
                Put(path, stamp, null, content);
                return content;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _entries.Clear();
                _lru.Clear();
            }
        }

        /// <summary>Moves an existing entry to the MRU position (the head of the LRU).</summary>
        private void Touch(CacheEntry entry)
        {
            if (entry.Node.List != null)
            {
                _lru.Remove(entry.Node);
            }
            _lru.AddFirst(entry.Node);
        }

        /// <summary>
        /// Inserts (or overwrites) an entry at the MRU position, dropping any stale node for the
        /// same path first, then evicts past the cap.
        /// </summary>
        private void Put(string path, DateTime stamp, string[]? lines, string? content)
        {
            if (_entries.TryGetValue(path, out CacheEntry existing) && existing.Node.List != null)
            {
                _lru.Remove(existing.Node);
            }
            LinkedListNode<string> node = _lru.AddFirst(path);
            _entries[path] = new CacheEntry(stamp, lines, content, node);
            EvictIfNeeded();
        }

        /// <summary>Evicts the least-recently-used entry when the cap is exceeded (M13/m9).</summary>
        private void EvictIfNeeded()
        {
            if (_maxEntries == null)
            {
                return;
            }
            while (_entries.Count > _maxEntries.Value)
            {
                LinkedListNode<string>? tail = _lru.Last;
                if (tail == null)
                {
                    break;
                }
                _lru.RemoveLast();
                _entries.Remove(tail.Value);
            }
        }

        private sealed class CacheEntry
        {
            public CacheEntry(DateTime timestamp, string[]? lines, string? content, LinkedListNode<string> node)
            {
                Timestamp = timestamp;
                Lines = lines;
                Content = content;
                Node = node;
            }

            public DateTime Timestamp { get; }
            public string[]? Lines { get; }
            public string? Content { get; }
            public LinkedListNode<string> Node { get; }
        }
    }
}
