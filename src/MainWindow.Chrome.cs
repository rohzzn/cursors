using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Cursors;

/// <summary>
/// Custom title bar that keeps native behaviour: DWM shadow and rounded corners, Aero Snap,
/// Windows 11 snap layouts on the maximize button, and resize borders.
/// </summary>
internal sealed partial class MainWindow
{
    private const int AnimationFrameMs = 33;
    private long _animationFrame = -1;

    private int _captionHover, _captionPressed; // HT* codes, 0 = none
    private Smooth _minHover, _maxHover, _closeHover;
    private bool _windowActive = true;
    private bool _inWindowPosChanged;

    private int FrameThickness
    {
        get
        {
            try
            {
                return Native.GetSystemMetricsForDpi(Native.SM_CYSIZEFRAME, (uint)_dpi) +
                       Native.GetSystemMetricsForDpi(Native.SM_CXPADDEDBORDER, (uint)_dpi);
            }
            catch (EntryPointNotFoundException)
            {
                return (int)(8 * S);
            }
        }
    }

    /// <summary>
    /// After a restore from maximized, Form's WM_WINDOWPOSCHANGED handler re-applies the saved size by converting the
    /// client size back to a window size as if the window had a standard caption, which would grow this caption-less
    /// window on every restore. Windows has already put the window at its real normal bounds by then, so keep those.
    /// </summary>
    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        if (_inWindowPosChanged && IsHandleCreated && !Native.IsZoomed(Handle) && !Native.IsIconic(Handle)) return;
        base.SetBoundsCore(x, y, width, height, specified);
    }

    private Rectangle CaptionButtonRect(int hitCode)
    {
        int w = (int)Math.Round(CaptionButtonWidth * S);
        int slot = hitCode == Native.HTCLOSE ? 1 : hitCode == Native.HTMAXBUTTON ? 2 : 3;
        return new Rectangle(ClientSize.Width - w * slot, 0, w, TitleBarPx);
    }

    private static bool IsCaptionButton(int ht) => ht == Native.HTMINBUTTON || ht == Native.HTMAXBUTTON || ht == Native.HTCLOSE;

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Native.WM_NCCALCSIZE when m.WParam != IntPtr.Zero:
            {
                var p = Marshal.PtrToStructure<Native.NCCALCSIZE_PARAMS>(m.LParam);
                int top = p.rgrc0.Top;
                base.WndProc(ref m);
                p = Marshal.PtrToStructure<Native.NCCALCSIZE_PARAMS>(m.LParam);
                // Drop the caption but keep the side/bottom frame; maximized windows overhang the monitor by one frame.
                p.rgrc0.Top = top + (Native.IsZoomed(Handle) ? FrameThickness : 0);
                Marshal.StructureToPtr(p, m.LParam, false);
                m.Result = IntPtr.Zero;
                return;
            }

            case Native.WM_NCHITTEST:
                base.WndProc(ref m);
                if ((int)m.Result == Native.HTCLIENT) m.Result = (IntPtr)HitTestTitleBar(m.LParam);
                return;

            case Native.WM_NCACTIVATE:
                _windowActive = m.WParam != IntPtr.Zero;
                Invalidate(new Rectangle(0, 0, ClientSize.Width, TitleBarPx));
                m.LParam = new IntPtr(-1); // don't let DefWindowProc repaint a caption over our client area
                base.WndProc(ref m);
                return;

            case Native.WM_NCMOUSEMOVE:
            {
                int ht = (int)m.WParam;
                SetCaptionHover(IsCaptionButton(ht) ? ht : 0);
                if (IsCaptionButton(ht))
                {
                    var tme = new Native.TRACKMOUSEEVENT
                    {
                        cbSize = Marshal.SizeOf<Native.TRACKMOUSEEVENT>(),
                        dwFlags = Native.TME_LEAVE | Native.TME_NONCLIENT,
                        hwndTrack = Handle,
                    };
                    Native.TrackMouseEvent(ref tme);
                    m.Result = IntPtr.Zero;
                    return;
                }
                break;
            }

            case Native.WM_NCLBUTTONDOWN:
            case Native.WM_NCLBUTTONDBLCLK:
                if (IsCaptionButton((int)m.WParam))
                {
                    _captionPressed = (int)m.WParam;
                    Invalidate(CaptionButtonRect(_captionPressed));
                    m.Result = IntPtr.Zero;
                    return;
                }
                _captionPressed = 0;
                break;

            case Native.WM_NCLBUTTONUP:
                if (IsCaptionButton((int)m.WParam))
                {
                    int pressed = _captionPressed;
                    _captionPressed = 0;
                    Invalidate(new Rectangle(0, 0, ClientSize.Width, TitleBarPx));
                    if (pressed == (int)m.WParam) PerformCaptionAction(pressed);
                    m.Result = IntPtr.Zero;
                    return;
                }
                _captionPressed = 0;
                break;

            case Native.WM_NCMOUSELEAVE:
                _captionPressed = 0;
                SetCaptionHover(0);
                break;

            case Native.WM_DPICHANGED:
            {
                var r = Marshal.PtrToStructure<Native.RECT>(m.LParam);
                UpdateDpi((int)(m.WParam.ToInt64() & 0xFFFF));
                Native.SetWindowPos(Handle, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                    Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                m.Result = IntPtr.Zero;
                return;
            }

            case Native.WM_WINDOWPOSCHANGED:
                _inWindowPosChanged = true;
                try
                {
                    base.WndProc(ref m);
                }
                finally
                {
                    _inWindowPosChanged = false;
                }
                return;

            case Native.WM_SETTINGCHANGE when m.WParam.ToInt64() == Native.SPI_SETCURSORS:
                BeginInvoke((Action)RefreshActiveFromSystem);
                break;
        }
        base.WndProc(ref m);
    }

    private int HitTestTitleBar(IntPtr lParam)
    {
        long lp = lParam.ToInt64();
        var pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
        int frame = FrameThickness;
        bool maximized = Native.IsZoomed(Handle);

        if (!maximized && pt.Y < frame)
        {
            if (pt.X < frame * 2) return Native.HTTOPLEFT;
            if (pt.X >= ClientSize.Width - frame * 2) return Native.HTTOPRIGHT;
            return Native.HTTOP;
        }
        if (pt.Y >= ViewportTopPx || (pt.Y >= TitleBarPx && pt.X < SidebarPx)) return Native.HTCLIENT;
        if (pt.Y >= TitleBarPx)
        {
            // Empty space in the header row drags the window too.
            return SortButtonRect().Contains(pt) || FiltersButtonRect().Contains(pt) ? Native.HTCLIENT : Native.HTCAPTION;
        }

        foreach (int ht in new[] { Native.HTCLOSE, Native.HTMAXBUTTON, Native.HTMINBUTTON })
            if (CaptionButtonRect(ht).Contains(pt)) return ht;
        if (RestoreButtonRect().Contains(pt) || SearchRect().Contains(pt)) return Native.HTCLIENT;
        return Native.HTCAPTION;
    }

    private void SetCaptionHover(int ht)
    {
        if (_captionHover == ht) return;
        _captionHover = ht;
        _minHover.Target = ht == Native.HTMINBUTTON ? 1 : 0;
        _maxHover.Target = ht == Native.HTMAXBUTTON ? 1 : 0;
        _closeHover.Target = ht == Native.HTCLOSE ? 1 : 0;
        StartAnimation();
    }

    private void PerformCaptionAction(int ht)
    {
        switch (ht)
        {
            // Let Windows track the normal placement. WinForms' WindowState setter re-derives the restored size assuming a
            // standard caption, which would grow this caption-less window on every maximize/restore.
            case Native.HTMINBUTTON:
                Native.ShowWindow(Handle, 6); // SW_MINIMIZE
                break;
            case Native.HTMAXBUTTON:
                Native.ShowWindow(Handle, Native.IsZoomed(Handle) ? 9 : 3); // SW_RESTORE : SW_MAXIMIZE
                break;
            case Native.HTCLOSE:
                Close();
                break;
        }
    }

    private bool StepChrome(float dt)
    {
        bool busy = false;
        if (_minHover.Step(dt, 35) | _maxHover.Step(dt, 35) | _closeHover.Step(dt, 35))
        {
            busy = true;
            Invalidate(new Rectangle(ClientSize.Width - (int)(CaptionButtonWidth * S * 3) - 2, 0, (int)(CaptionButtonWidth * S * 3) + 2, TitleBarPx));
        }
        if (_restoreHover.Step(dt, 45) | _restorePress.Step(dt, 28))
        {
            busy = true;
            var r = RestoreButtonRect();
            r.Inflate(2, 2);
            Invalidate(r);
        }
        if (StepSearch(dt)) busy = true;
        if (_scrollbarHover.Step(dt, 60))
        {
            busy = true;
            Invalidate(new Rectangle(ClientSize.Width - (int)(14 * S), ViewportTopPx, (int)(14 * S), ClientSize.Height - ViewportTopPx));
        }
        return busy;
    }
}
