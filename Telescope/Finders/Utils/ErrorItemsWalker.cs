using EnvDTE80;
using System;

namespace Telescope.Finders
{
    /// <summary>
    /// Shared per-item walk over the VS Error List (m33/BP-24): iterates the 1-based
    /// <see cref="ErrorItems"/> collection and invokes the action for each readable item.
    /// A throwing item (a COM read failure or an action exception) is skipped — the rest
    /// survive — so a single bad row can never abort the walk. Used by both the MyExtension
    /// <c>ErrorListGatherer</c> and the Telescope <c>CodeIssuesFinder</c> (the cross-slice
    /// duplication this helper removes).
    /// </summary>
    internal static class ErrorItemsWalker
    {
        /// <summary>
        /// Iterates <paramref name="items"/> (1..Count) and invokes <paramref name="action"/>
        /// for each item. Per-item try/catch: a throwing item is skipped, the rest survive.
        /// </summary>
        internal static void ForEach(ErrorItems items, Action<ErrorItem> action)
        {
            if (items == null)
            {
                return;
            }

            int count = items.Count;
            for (int i = 1; i <= count; i++)
            {
                try
                {
                    ErrorItem item = items.Item(i);
                    action(item);
                }
                catch
                {
                    // skip an item that can't be read
                }
            }
        }
    }
}
