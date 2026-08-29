using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System.Drawing;

namespace CardinalMovment;

public enum Direction { Left, Right, Up, Down }

public class CardinalMovment()
{

    public void Navigate(Direction dir)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var uiShell = Package.GetGlobalService(typeof(SVsUIShell)) as IVsUIShell;
        var monitor = Package.GetGlobalService(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;

        // 1. Active frame
        monitor.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_WindowFrame, out object activeObj);
        if (activeObj is not IVsWindowFrame activeFrame) return;

        Rectangle activeRect = GetScreenRect(activeFrame);
        if (activeRect.IsEmpty) return;

        // 2. Collect all other visible frames
        var candidates = new List<(IVsWindowFrame Frame, Rectangle Rect)>();
        CollectFrames(uiShell, candidates, activeFrame);

        // 3. Find best candidate
        IVsWindowFrame best = FindBestCandidate(activeRect, candidates, dir);
        if (best != null)
            best.Show();   // or ShowNoActivate()
    }

    private static Rectangle GetScreenRect(IVsWindowFrame frame)
    {
        if (frame is IVsWindowFrame4 f4)
        {
            f4.GetWindowScreenRect(out int l, out int t, out int w, out int h);
            return new Rectangle(l, t, w, h);
        }
        // fallback to GetFramePos ...
        return Rectangle.Empty;
    }

    private void CollectFrames(IVsUIShell shell, List<(IVsWindowFrame, Rectangle)> list, IVsWindowFrame exclude)
    {
        void AddFromEnum(IEnumWindowFrames enumerator)
        {
            var frames = new IVsWindowFrame[1];
            while (enumerator.Next(1, frames, out uint fetched) == VSConstants.S_OK && fetched == 1)
            {
                var f = frames[0];
                if (f == null || f == exclude) continue;
                if (ErrorHandler.Failed(f.IsVisible()) || f.IsVisible() == 0) continue;

                var r = GetScreenRect(f);
                if (!r.IsEmpty)
                    list.Add((f, r));
            }
        }

        shell.GetDocumentWindowEnum(out var docs);
        shell.GetToolWindowEnum(out var tools);
        AddFromEnum(docs);
        AddFromEnum(tools);
    }
}
