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
        private readonly Dictionary<int, string> _map = new();

        public void Store(int version, string text)
        {
            _map[version] = text;
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
