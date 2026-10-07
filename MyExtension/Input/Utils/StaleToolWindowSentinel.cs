namespace MyExtension.Input
{
    /// <summary>
    /// Caches whether the test-only 'stale-toolwindow' fault is injected. The harness creates a
    /// sentinel file under NEOVISUAL_LOG_DIR to force the Solution Explorer frame as current even
    /// when it is not; the file is only stat'd on <see cref="Refresh"/>, so the per-key path reads
    /// the cached <see cref="IsStale"/> instead of calling <c>File.Exists</c> for every key-down.
    /// </summary>
    internal sealed class StaleToolWindowSentinel
    {
        /// <summary>
        /// M1 (BP-3): whether the stale-toolwindow fault is configured for this session — the
        /// harness sets <c>NEOVISUAL_LOG_DIR</c> (the sentinel file lives under it). Read FRESH
        /// (not cached at static-init) so tests can set/unset the env var around construction.
        /// </summary>
        internal static bool IsConfigured =>
            !string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("NEOVISUAL_LOG_DIR"));

        private readonly string? _path;
        private bool _isStale;

        public StaleToolWindowSentinel(string? path)
        {
            _path = path;
        }

        public bool IsStale => _isStale;

        /// <summary>Re-stats the sentinel file. Returns true when <see cref="IsStale"/> changed.</summary>
        public bool Refresh()
        {
            bool was = _isStale;
            _isStale = _path != null && System.IO.File.Exists(_path);
            return _isStale != was;
        }
    }
}
