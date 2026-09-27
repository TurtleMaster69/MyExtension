using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using System;

namespace CardinalNavigation
{
    /// <summary>
    /// implements various IVsWindowFrame interfaces for enhanced window functionality
    /// relative to the DTE.
    /// </summary>
    class IVsFrameView : IVsWindowFrame4, IVsWindowFrame, IVsWindowFrameNotify
    {

        private int m_screenLeft, m_screenTop, m_screenWidth, m_screenHeight;

        private IVsWindowFrame4 m_Frame2;
        private IVsWindowFrame m_Frame;

        /// <summary>
        /// getter for base internal frame; useful for equality checks
        /// </summary>
        public IVsWindowFrame internalFrame { get => m_Frame; }

        public IVsFrameView(IVsWindowFrame frame)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

            if (frame == null)
            {
                ErrorHandler.ThrowOnFailure(VSConstants.E_FAIL);
            }

            m_Frame = frame;
            m_Frame2 = (IVsWindowFrame4)frame;

            m_Frame2.GetWindowScreenRect(out m_screenLeft, out m_screenTop, out m_screenWidth, out m_screenHeight);
        }

        /// <summary>
        /// returns true if this window is docked and tabbed out of view.
        /// </summary>
        /// <returns></returns>
        public bool IsTabbedAndInvisible()
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            int pfOnScreen = 0;
            ErrorHandler.ThrowOnFailure(this.IsOnScreen(out pfOnScreen));
            return pfOnScreen == 0;
        }

        public int Show()
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            return m_Frame.Show();
        }

        public int Hide()
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            return m_Frame.Hide();
        }

        public int IsVisible()
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            return m_Frame.IsVisible();
        }

        public int ShowNoActivate()
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            return m_Frame.ShowNoActivate();
        }

        public int CloseFrame(uint grfSaveOptions)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            return m_Frame.CloseFrame(grfSaveOptions);
        }

        public int SetFramePos(VSSETFRAMEPOS dwSFP, ref Guid rguidRelativeTo, int x, int y, int cx, int cy)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            return m_Frame.SetFramePos(dwSFP, ref rguidRelativeTo, x, y, cx, cy);
        }

        public int GetFramePos(VSSETFRAMEPOS[] pdwSFP, out Guid pguidRelativeTo, out int px, out int py, out int pcx, out int pcy)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            return m_Frame.GetFramePos(pdwSFP, out pguidRelativeTo, out px, out py, out pcx, out pcy);
        }

        public int GetProperty(int propid, out object pvar)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            return m_Frame.GetProperty(propid, out pvar);
        }

        public int SetProperty(int propid, object var)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            return m_Frame.SetProperty(propid, var);
        }

        public int GetGuidProperty(int propid, out Guid pguid)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            return m_Frame.GetGuidProperty(propid, out pguid);
        }

        public int SetGuidProperty(int propid, ref Guid rguid)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            return m_Frame.SetGuidProperty(propid, ref rguid);
        }

        public int QueryViewInterface(ref Guid riid, out IntPtr ppv)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            return m_Frame.QueryViewInterface(ref riid, out ppv);
        }

        public int IsOnScreen(out int pfOnScreen)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            return m_Frame.IsOnScreen(out pfOnScreen);
        }

        public int OnShow(int fShow)
        {
            throw new NotImplementedException();
        }

        public int OnMove()
        {
            throw new NotImplementedException();
        }

        public int OnSize()
        {
            throw new NotImplementedException();
        }

        public int OnDockableChange(int fDockable)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// returns what's actually being rendered on the screen
        /// </summary>
        /// <param name="screenLeft">x coordinate</param>
        /// <param name="screenTop">y coordinate</param>
        /// <param name="screenWidth">width; top left corner is (x + width)</param>
        /// <param name="screenHeight">height; bottom left corner is (y + height)</param>
        /// <returns>was the ScreenRect sucessfully retrieved?></returns>
        public bool GetWindowScreenRect(out int screenLeft, out int screenTop, out int screenWidth, out int screenHeight)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            var result = m_Frame2.GetWindowScreenRect(out m_screenLeft, out m_screenTop, out m_screenWidth, out m_screenHeight);
            screenLeft = m_screenLeft;
            screenTop = m_screenTop;
            screenWidth = m_screenWidth;
            screenHeight = m_screenHeight;
            return result;
        }
    }
}

