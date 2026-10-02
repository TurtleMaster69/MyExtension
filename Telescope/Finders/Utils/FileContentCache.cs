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

        // M13: monotonically increasing access counter for LRU eviction (the injected timestamp
        // delegate may return a constant, so wall-clock cannot drive the LRU order).
        private long _accessCounter;

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
            DateTime stamp = _timestamp(path);
            if (_entries.TryGetValue(path, out CacheEntry entry) && entry.Timestamp == stamp && entry.Lines != null)
            {
                entry.LastAccess = ++_accessCounter;
                return entry.Lines;
            }
            string[] lines = _reader(path);
            _entries[path] = new CacheEntry(stamp, lines, ++_accessCounter);
            EvictIfNeeded();
            return lines;
        }

        /// <summary>
        /// Returns the cached full file content when the file's <c>LastWriteTimeUtc</c> is
        /// unchanged, else re-reads and caches it (M5 — the preview re-reads/re-tokenizes only on
        /// change). The exact content string is preserved (no line-ending normalization).
        /// </summary>
        public string GetContent(string path)
        {
            DateTime stamp = _timestamp(path);
            if (_entries.TryGetValue(path, out CacheEntry entry) && entry.Timestamp == stamp && entry.Content != null)
            {
                entry.LastAccess = ++_accessCounter;
                return entry.Content;
            }
            string content = _contentReader(path);
            _entries[path] = new CacheEntry(stamp, content, ++_accessCounter);
            EvictIfNeeded();
            return content;
        }

        public void Clear()
        {
            _entries.Clear();
            _accessCounter = 0;
        }

        /// <summary>Evicts the least-recently-used entry when the cap is exceeded (M13).</summary>
        private void EvictIfNeeded()
        {
            if (_maxEntries == null)
            {
                return;
            }
            while (_entries.Count > _maxEntries.Value)
            {
                string? oldestKey = null;
                long oldestAccess = long.MaxValue;
                foreach (var pair in _entries)
                {
                    if (pair.Value.LastAccess < oldestAccess)
                    {
                        oldestAccess = pair.Value.LastAccess;
                        oldestKey = pair.Key;
                    }
                }
                if (oldestKey == null)
                {
                    break;
                }
                _entries.Remove(oldestKey);
            }
        }

        private sealed class CacheEntry
        {
            public CacheEntry(DateTime timestamp, string[] lines, long lastAccess)
                : this(timestamp, lines, null, lastAccess)
            {
            }

            public CacheEntry(DateTime timestamp, string content, long lastAccess)
                : this(timestamp, null, content, lastAccess)
            {
            }

            private CacheEntry(DateTime timestamp, string[]? lines, string? content, long lastAccess)
            {
                Timestamp = timestamp;
                Lines = lines;
                Content = content;
                LastAccess = lastAccess;
            }

            public DateTime Timestamp { get; }
            public string[]? Lines { get; }
            public string? Content { get; }
            public long LastAccess { get; set; }
        }
    }
}
