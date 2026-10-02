using System;
using System.Collections.Generic;
using System.IO;

namespace Telescope.Overlay
{
    /// <summary>
    /// Mtime-keyed cache of the tokenized syntax segments for a preview file, so
    /// <see cref="PreviewRenderer"/> re-tokenizes only when the file's
    /// <c>LastWriteTimeUtc</c> changes (the FlowDocument rebuild stays the renderer's job — this
    /// cache holds NO WPF types). Injected timestamp/content-reader delegates keep the unit tests
    /// hermetic (mirrors <see cref="Telescope.Finders.FileContentCache"/>).
    /// </summary>
    internal sealed class PreviewTokenCache
    {
        private readonly Func<string, DateTime> _timestamp;
        private readonly Func<string, string> _contentReader;

        private readonly Dictionary<string, CacheEntry> _entries =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        public PreviewTokenCache(Func<string, DateTime>? timestamp = null, Func<string, string>? contentReader = null)
        {
            _timestamp = timestamp ?? File.GetLastWriteTimeUtc;
            _contentReader = contentReader ?? File.ReadAllText;
        }

        /// <summary>
        /// Returns the cached segments when the file's <c>LastWriteTimeUtc</c> is unchanged, else
        /// re-reads + re-tokenizes and caches them. The tokenizer is invoked only on a cache miss.
        /// </summary>
        public IReadOnlyList<SyntaxSegment> GetSegments(string path, Func<string, IReadOnlyList<SyntaxSegment>> tokenize)
        {
            DateTime stamp = _timestamp(path);
            if (_entries.TryGetValue(path, out CacheEntry entry) && entry.Timestamp == stamp)
            {
                return entry.Segments;
            }
            string content = _contentReader(path);
            var segments = tokenize(content);
            _entries[path] = new CacheEntry(stamp, segments);
            return segments;
        }

        private sealed class CacheEntry
        {
            public CacheEntry(DateTime timestamp, IReadOnlyList<SyntaxSegment> segments)
            {
                Timestamp = timestamp;
                Segments = segments;
            }

            public DateTime Timestamp { get; }
            public IReadOnlyList<SyntaxSegment> Segments { get; }
        }
    }
}
