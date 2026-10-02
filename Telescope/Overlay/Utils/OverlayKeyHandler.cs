using System;

namespace Telescope.Overlay
{
    /// <summary>
    /// A normalized key gesture for the overlay, mapped from the WPF <c>Key</c> by the overlay so
    /// the logic stays free of WPF/VS dependencies and can be unit-tested hermetically.
    /// </summary>
    internal enum OverlayKey
    {
        Other,
        Escape,
        Q,
        Enter,
        Up,
        Down,
        J,
        K,
        G,      // plain 'g'; shift+G is encoded as ShiftG
        ShiftG, // 'G' (move to last)
        I,
        A,
        CtrlH, // Ctrl+H: move focus to the results list
        CtrlL, // Ctrl+L: move focus to the file preview
    }

    /// <summary>
    /// The outcome of feeding a key to the overlay logic. <see cref="OverlayKeyHandler.Handle"/>
    /// mutates its internal state (mode, selection, pending-gg) and returns the UI action the
    /// overlay must perform. Everything here is pure state; the WPF overlay only applies the
    /// returned action to the actual controls.
    /// </summary>
    internal enum OverlayAction
    {
        None,
        Close,            // overlay should close
        SelectCurrent,    // overlay should invoke OnSelected on the selected entry
        EnterInsert,      // switch to insert mode, caret at current position
        EnterInsertAppend,// switch to insert mode, caret at end
        EnterInsertStart, // switch to insert mode, caret at start
        EnterNormal,      // switch to normal mode
        MoveDown,         // selection moved down (wrap)
        MoveUp,           // selection moved up (wrap)
        MoveToFirst,
        MoveToLast,
    }

    /// <summary>
    /// Where the insert-mode caret should be placed when entering insert mode.
    /// </summary>
    internal enum CaretPlacement
    {
        Current, // i: keep the caret where it is (clamped to the text)
        End,     // a: caret at the end of the text
        Start,   // I: caret at the start of the text
    }

    /// <summary>
    /// Dependency-free state machine for the Telescope overlay's vim-style key handling:
    /// insert/normal mode, j/k/gg/G selection movement, and which key maps to which action.
    /// Extracted from <see cref="TelescopeOverlay"/> so navigation and mode behavior can be
    /// unit-tested without a WPF window or Visual Studio.
    /// </summary>
    internal sealed class OverlayKeyHandler
    {
        private bool _isNormalMode;
        private bool _gPending;
        private int _selectedIndex;
        private int _resultCount;

        /// <summary>True while the handler is in normal mode.</summary>
        public bool IsNormalMode => _isNormalMode;

        /// <summary>Index of the currently selected result.</summary>
        public int SelectedIndex => _selectedIndex;

        /// <summary>Replaces the results count and clamps the selection into range.</summary>
        public void SetResults(int count)
        {
            _resultCount = Math.Max(0, count);
            if (_resultCount == 0)
            {
                _selectedIndex = 0;
            }
            else if (_selectedIndex >= _resultCount)
            {
                _selectedIndex = _resultCount - 1;
            }
        }

        /// <summary>Starts a fresh overlay: insert mode, first entry selected, no pending gg.</summary>
        public void Reset()
        {
            _isNormalMode = false;
            _gPending = false;
            _selectedIndex = 0;
            _resultCount = 0;
        }

        /// <summary>
        /// Routes a key through the vim state machine. Mutates mode/selection state and returns
        /// the action the overlay should perform. Keys that do not map to a Telescope action
        /// return <see cref="OverlayAction.None"/> (they fall through to the prompt/OS).
        /// </summary>
        public OverlayAction Handle(OverlayKey key)
        {
            if (_isNormalMode)
            {
                return HandleNormal(key);
            }
            return HandleInsert(key);
        }

        private OverlayAction HandleInsert(OverlayKey key)
        {
            switch (key)
            {
                case OverlayKey.Enter:
                    return OverlayAction.SelectCurrent;
                case OverlayKey.Escape:
                    _isNormalMode = true;
                    _gPending = false;
                    return OverlayAction.EnterNormal;
                default:
                    return OverlayAction.None;
            }
        }

        private OverlayAction HandleNormal(OverlayKey key)
        {
            switch (key)
            {
                case OverlayKey.Escape:
                case OverlayKey.Q:
                    _gPending = false;
                    return OverlayAction.Close;

                case OverlayKey.Enter:
                    _gPending = false;
                    return OverlayAction.SelectCurrent;

                case OverlayKey.Down:
                case OverlayKey.J:
                    _gPending = false;
                    MoveSelection(1);
                    return OverlayAction.MoveDown;

                case OverlayKey.Up:
                case OverlayKey.K:
                    _gPending = false;
                    MoveSelection(-1);
                    return OverlayAction.MoveUp;

                case OverlayKey.G:
                    if (_gPending)
                    {
                        _gPending = false;
                        MoveSelectionToFirst();
                        return OverlayAction.MoveToFirst;
                    }
                    _gPending = true; // awaiting a second 'g' for "gg"
                    return OverlayAction.None;

                case OverlayKey.ShiftG:
                    _gPending = false;
                    MoveSelectionToLast();
                    return OverlayAction.MoveToLast;

                case OverlayKey.I:
                    return EnterInsertMode(CaretPlacement.Current);
                case OverlayKey.A:
                    return EnterInsertMode(CaretPlacement.End);

                default:
                    // Unrecognized keys cancel a pending gg and fall through.
                    _gPending = false;
                    return OverlayAction.None;
            }
        }

        internal OverlayAction EnterInsertMode(CaretPlacement placement)
        {
            _isNormalMode = false;
            _gPending = false;
            switch (placement)
            {
                case CaretPlacement.End:
                    return OverlayAction.EnterInsertAppend;
                case CaretPlacement.Start:
                    return OverlayAction.EnterInsertStart;
                default:
                    return OverlayAction.EnterInsert;
            }
        }

        private void MoveSelection(int delta)
        {
            if (_resultCount == 0)
            {
                return;
            }
            _selectedIndex = (_selectedIndex + delta + _resultCount) % _resultCount;
        }

        private void MoveSelectionToFirst()
        {
            if (_resultCount == 0)
            {
                return;
            }
            _selectedIndex = 0;
        }

        private void MoveSelectionToLast()
        {
            if (_resultCount == 0)
            {
                return;
            }
            _selectedIndex = _resultCount - 1;
        }
    }
}