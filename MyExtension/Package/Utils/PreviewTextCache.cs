using System.Collections.Generic;

namespace MyExtension.Package
{
    /// <summary>
    /// Pure version-keyed cache for the preview buffer text (A2): maps an
    /// <c>ITextSnapshot.Version.VersionNumber</c> to the materialized text, so
    /// <see cref="PreviewEditorHost.Show"/> does not call <c>CurrentSnapshot.GetText()</c> (a
    /// full-buffer copy) on every selection move — only on a snapshot-version change (a rebuild).
    /// </summary>
    internal sealed class PreviewTextCache
    {
        // m7 (BP-8): the cache is bounded — a long overlay session on a frequently-edited buffer
        // must not grow unbounded. Store evicts the lowest version when the capacity is exceeded
        // (the oldest snapshot is the least likely to be re-read).
        private const int MaxCapacity = 8;

        private readonly Dictionary<int, string> _map = new();

        public void Store(int version, string text)
        {
            _map[version] = text;
            if (_map.Count > MaxCapacity)
            {
                int oldest = int.MaxValue;
                foreach (int v in _map.Keys)
                {
                    if (v < oldest)
                    {
                        oldest = v;
                    }
                }
                _map.Remove(oldest);
            }
        }

        public string? Get(int version)
        {
            return _map.TryGetValue(version, out var text) ? text : null;
        }

        public void Clear()
        {
            _map.Clear();
        }
    }
}
