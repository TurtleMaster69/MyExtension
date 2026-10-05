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
    }
}
