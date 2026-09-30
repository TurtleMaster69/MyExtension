using System;
using System.Collections.Generic;
using System.IO;

namespace Telescope
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

        private readonly Dictionary<string, CacheEntry> _entries =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        public FileContentCache(Func<string, DateTime>? timestamp = null, Func<string, string[]>? reader = null)
        {
            _timestamp = timestamp ?? File.GetLastWriteTimeUtc;
            _reader = reader ?? File.ReadAllLines;
        }

        /// <summary>
        /// Returns the cached lines when the file's <c>LastWriteTimeUtc</c> is unchanged, else
        /// re-reads and caches them.
        /// </summary>
        public string[] GetLines(string path)
        {
            DateTime stamp = _timestamp(path);
            if (_entries.TryGetValue(path, out CacheEntry entry) && entry.Timestamp == stamp)
            {
                return entry.Lines;
            }
            string[] lines = _reader(path);
            _entries[path] = new CacheEntry(stamp, lines);
            return lines;
        }

        public void Clear() => _entries.Clear();

        private sealed class CacheEntry
        {
            public CacheEntry(DateTime timestamp, string[] lines)
            {
                Timestamp = timestamp;
                Lines = lines;
            }

            public DateTime Timestamp { get; }
            public string[] Lines { get; }
        }
    }
}
