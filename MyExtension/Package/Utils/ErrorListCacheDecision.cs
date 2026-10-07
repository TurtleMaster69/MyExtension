namespace MyExtension.Package
{
    /// <summary>
    /// Pure cache-decision helper for the Error List gather (A3): DTE <c>ErrorItems</c> exposes NO
    /// version counter, so a count-keyed cache is weak (same count, different items after a build).
    /// The gather uses a short-TTL cache and delegates the freshness decision here — a cache is
    /// fresh while its age is strictly below the TTL (age &lt; ttl).
    /// </summary>
    internal static class ErrorListCacheDecision
    {
        public static bool IsFresh(long ageMs, long ttlMs) => ageMs < ttlMs;

        /// <summary>
        /// m8 (BP-9): whether an event name invalidates the Error List cache — a build, a document
        /// save/open, or a window activation can change the Error List contents; a selection change
        /// cannot. The single source for which events force a fresh gather.
        /// </summary>
        internal static bool ShouldInvalidateOnEvent(string eventName)
        {
            return eventName == "build-done"
                || eventName == "document-saved"
                || eventName == "document-opened"
                || eventName == "window-activated";
        }
    }
}
