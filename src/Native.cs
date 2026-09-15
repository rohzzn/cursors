using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Cursors;

internal static class Native
{
    public const int WM_WINDOWPOSCHANGED = 0x0047;
    public const int WM_NCCALCSIZE = 0x0083;
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_NCACTIVATE = 0x0086;
    public const int WM_NCMOUSEMOVE = 0x00A0;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int WM_NCLBUTTONUP = 0x00A2;
    public const int WM_NCLBUTTONDBLCLK = 0x00A3;
    public const int WM_NCMOUSELEAVE = 0x02A2;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_SETTINGCHANGE = 0x001A;

    public const int HTCLIENT = 1;
    public const int HTCAPTION = 2;
    public const int HTMINBUTTON = 8;
    public const int HTMAXBUTTON = 9;
    public const int HTTOP = 12;
    public const int HTTOPLEFT = 13;
    public const int HTTOPRIGHT = 14;
    public const int HTCLOSE = 20;

    public const int SM_CXSIZEFRAME = 32;
    public const int SM_CYSIZEFRAME = 33;
    public const int SM_CXPADDEDBORDER = 92;

    public const uint SPI_SETCURSORS = 0x0057;
    public const uint SPIF_UPDATEINIFILE = 0x01;
    public const uint SPIF_SENDCHANGE = 0x02;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;

    public const uint TME_LEAVE = 0x02;
    public const uint TME_NONCLIENT = 0x10;

    public const uint MF_STRING = 0x0000;
    public const uint MF_GRAYED = 0x0001;
    public const uint MF_CHECKED = 0x0008;
    public const uint MF_SEPARATOR = 0x0800;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_RETURNCMD = 0x0100;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NCCALCSIZE_PARAMS
    {
        public RECT rgrc0, rgrc1, rgrc2;
        public IntPtr lppos;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int Left, Right, Top, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TRACKMOUSEEVENT
    {
        public int cbSize;
        public uint dwFlags;
        public IntPtr hwndTrack;
        public uint dwHoverTime;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint action, uint param, IntPtr pv, uint winIni);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT tme);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);

    [DllImport("user32.dll")]
    public static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);

    [DllImport("user32.dll")]
    public static extern bool DestroyMenu(IntPtr menu);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string source, StringBuilder buffer, int size, IntPtr reserved);

    [DllImport("uxtheme.dll", EntryPoint = "#133")]
    private static extern bool AllowDarkModeForWindow(IntPtr hwnd, bool allow);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();

    public static int Build => Environment.OSVersion.Version.Build;

    public static string LoadIndirectString(string source)
    {
        var sb = new StringBuilder(512);
        return SHLoadIndirectString(source, sb, sb.Capacity, IntPtr.Zero) == 0 ? sb.ToString() : null;
    }

    /// <summary>Opts native popup menus and the system menu into the dark theme (Win10 1903+).</summary>
    public static void EnableDarkMenus()
    {
        if (Build < 18362) return;
        try
        {
            SetPreferredAppMode(2); // ForceDark
            FlushMenuThemes();
        }
        catch { }
    }

    public static void ApplyDarkFrame(IntPtr hwnd, int captionColorRgb)
    {
        int on = 1;
        try
        {
            if (Build >= 18362) AllowDarkModeForWindow(hwnd, true);
        }
        catch { }
        // DWMWA_USE_IMMERSIVE_DARK_MODE is 20 on 20H1+, 19 on older builds.
        if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref on, 4);
        if (Build >= 22000)
        {
            int border = captionColorRgb; // COLORREF is 0x00BBGGRR
            DwmSetWindowAttribute(hwnd, 34, ref border, 4); // DWMWA_BORDER_COLOR
        }
        else
        {
            var margins = new MARGINS { Top = 1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }
    }

    public static int ToColorRef(System.Drawing.Color c) => c.R | (c.G << 8) | (c.B << 16);
}
