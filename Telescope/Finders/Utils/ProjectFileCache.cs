using System;
using System.Collections.Generic;

namespace Telescope.Finders
{
    /// <summary>
    /// Caches the DTE project-file enumeration per session so <see cref="CodeIssuesFinder"/>
    /// does not re-walk the solution tree on every open. Invalidated when the solution changes.
    /// </summary>
    internal sealed class ProjectFileCache
    {
        private IReadOnlyList<string>? _cached;
        private bool _dirty = true;

        public IReadOnlyList<string> Get(Func<IReadOnlyList<string>> enumerate)
        {
            if (_cached == null || _dirty)
            {
                _cached = enumerate();
                _dirty = false;
            }
            return _cached;
        }

        public void Invalidate()
        {
            _dirty = true;
        }
    }
}
