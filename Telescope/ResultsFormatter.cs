using System.Collections.Generic;
using System.Text;

namespace Telescope
{
    /// <summary>
    /// Pure formatting for the results pane. Kept as a standalone internal class (no WPF / VS
    /// dependencies) so it can be unit-tested hermetically by <c>tests/Telescope.Tests</c>.
    /// </summary>
    internal static class ResultsFormatter
    {
        /// <summary>
        /// Renders <paramref name="results"/> as a text list with a <c>&gt; </c> selection marker
        /// on <paramref name="selectedIndex"/> (one entry per line). Empty results produce an
        /// empty string.
        /// </summary>
        internal static string ToText(IReadOnlyList<FinderEntry> results, int selectedIndex)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < results.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('\n');
                }
                sb.Append(i == selectedIndex ? "> " : "  ");
                sb.Append(results[i].Display);
            }
            return sb.ToString();
        }
    }
}