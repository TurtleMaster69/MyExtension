using System;
using System.Collections.Generic;

namespace Telescope.Finders
{
    /// <summary>
    /// Caches the DTE project-file enumeration per session so <see cref="CodeIssuesFinder"/>
    /// does not re-walk the solution tree on every open. Invalidated when the solution changes,
    /// and additionally expires after a bounded TTL (N44/BP-58) so files added/removed within a
    /// solution are picked up without wiring a file-add/remove VS event.
    /// </summary>
    internal sealed class ProjectFileCache
    {
        private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(5);

        private readonly Func<DateTime> _clock;
        private readonly TimeSpan _ttl;

        private IReadOnlyList<string>? _cached;
        private DateTime _cachedAtUtc;
        private bool _dirty = true;

        public ProjectFileCache()
            : this(() => DateTime.UtcNow, DefaultTtl)
        {
        }

        /// <summary>Test seam: injects the clock + TTL so expiry is deterministic.</summary>
        internal ProjectFileCache(Func<DateTime> clock, TimeSpan ttl)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _ttl = ttl;
        }

        public IReadOnlyList<string> Get(Func<IReadOnlyList<string>> enumerate)
        {
            if (_cached == null || _dirty || IsExpired())
            {
                _cached = enumerate();
                _cachedAtUtc = _clock();
                _dirty = false;
            }
            return _cached;
        }

        public void Invalidate()
        {
            _dirty = true;
        }

        private bool IsExpired() => _clock() - _cachedAtUtc >= _ttl;
    }
}
