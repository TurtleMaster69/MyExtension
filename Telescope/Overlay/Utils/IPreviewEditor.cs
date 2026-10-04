using System.Windows;
using Telescope.Finders;

namespace Telescope.Overlay
{
    /// <summary>
    /// The preview pane's editor seam: the overlay delegates the REAL editor view (IWpfTextView)
    /// hosting to a host-supplied implementation, keeping the VS-SDK-coupled view creation OUT of
    /// the Telescope library (spec.md's layering note: new VS-coupled code goes in MyExtension).
    /// The overlay owns the motion model (<see cref="TextMotionNavigator"/>) and the preview slot;
    /// the implementation owns view creation/reuse (mtime-keyed), the caret application, and the
    /// preview diagnostics. Public like the other Telescope seams (<c>IFinder</c>,
    /// <c>IFileLocation</c>) so the host can implement it — the internal motion model never
    /// crosses this seam.
    /// </summary>
    public interface IPreviewEditor
    {
        /// <summary>
        /// Loads <paramref name="location"/>'s file into a read-only editor view (create-or-reuse
        /// by path + LastWriteTimeUtc) and returns the element to host plus the buffer's text (for
        /// the overlay's motion model). <see cref="PreviewEditorResult.Empty"/> = no preview (a
        /// reason is logged). UI thread only.
        /// </summary>
        PreviewEditorResult Show(IFileLocation location);

        /// <summary>Moves the view's caret to <paramref name="caretIndex"/> (the motion model's
        /// index) and scrolls it into view. No-op without a view.</summary>
        void ApplyCaret(int caretIndex);

        /// <summary>Gives the editor view keyboard focus (the preview focus target). No-op
        /// without a view.</summary>
        void Focus();

        /// <summary>Closes the view + disposes the document (overlay close). Idempotent. UI thread.</summary>
        void Dispose();
    }

    /// <summary>The Show result: the hosted element + the buffer text ("" when no preview).</summary>
    public sealed class PreviewEditorResult
    {
        public static PreviewEditorResult Empty { get; } = new PreviewEditorResult(null, string.Empty);

        public PreviewEditorResult(FrameworkElement? element, string text)
        {
            Element = element;
            Text = text ?? string.Empty;
        }

        /// <summary>The element to host in the preview slot (null = no preview).</summary>
        public FrameworkElement? Element { get; }

        /// <summary>The buffer's snapshot text — the overlay's motion model's text ("" when no preview).</summary>
        public string Text { get; }
    }
}
