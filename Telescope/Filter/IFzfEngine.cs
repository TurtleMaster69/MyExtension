using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Telescope.Filter
{
    /// <summary>
    /// Availability + filter seam for the fzf engine, so a finder can drive both the available
    /// (fuzzy filter) and unavailable (literal fallback) paths without a real subprocess.
    /// <see cref="FzfFilter"/> implements it.
    /// </summary>
    internal interface IFzfEngine
    {
        Task<bool> IsAvailableAsync();

        Task<IReadOnlyList<string>?> FilterAsync(IEnumerable<string> candidates, string query, CancellationToken ct);
    }
}
