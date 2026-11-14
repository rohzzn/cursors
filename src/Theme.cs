using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace Cursors;

/// <summary>How the app picks its light/dark palette. Auto follows the Windows app theme live.</summary>
internal enum AppTheme
{
    Auto,
    Light,
    Dark,
}

/// <summary>
/// The active palette. Colors are mutable fields (not consts) so the whole UI can be re-skinned at
/// runtime: every drawing path reads the field at paint time, and a switch just needs a repaint.
/// </summary>
internal static class Theme
{
    // ---- Theme state ---------------------------------------------------------------------------

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static bool _initialized;

    /// <summary>The user's choice, persisted in settings.ini.</summary>
    public static AppTheme Selection { get; private set; } = AppTheme.Auto;

    /// <summary>The palette actually in use (Auto resolved against the current system setting).</summary>
    public static bool IsDark { get; private set; }

    public static string StoreValue => Selection switch
    {
        AppTheme.Light => "light",
        AppTheme.Dark => "dark",
        _ => "auto",
    };

    /// <summary>
    /// Windows 10/11 store the app light/dark setting here. Older Windows has no such setting,
    /// so fall back to dark, which was the app's only look before themes were added.
    /// </summary>
    public static bool SystemUsesDark
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                if (key?.GetValue("AppsUseLightTheme") is int apps) return apps == 0;
                if (key?.GetValue("SystemUsesLightTheme") is int system) return system == 0;
            }
            catch { }
            return true;
        }
    }

    static Theme() => Load(null);

    /// <summary>Read once at startup, before any UI is created.</summary>
    public static void Load(string stored)
    {
        Selection = stored?.Trim().ToLowerInvariant() switch
        {
            "light" => AppTheme.Light,
            "dark" => AppTheme.Dark,
            _ => AppTheme.Auto,
        };
        Refresh();
    }

    public static void Set(AppTheme theme)
    {
        Selection = theme;
        Refresh();
    }

    /// <summary>Re-resolves Auto and rebuilds the palette. Returns true when light/dark actually changed.</summary>
    public static bool Refresh()
    {
        bool dark = Selection switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => SystemUsesDark,
        };
        if (_initialized && dark == IsDark) return false;
        _initialized = true;
        IsDark = dark;
        ApplyPalette(dark);
        return true;
    }

    // ---- Palette -------------------------------------------------------------------------------
    // These fields are assigned by ApplyPalette; do not add initializers here.

    public static Color Background;
    public static Color Card;
    public static Color CardHover;
    public static Color CardPressed;
    public static Color CardSelected;
    public static Color Border;
    public static Color BorderHover;
    public static Color BorderSelected;
    public static Color Text;
    public static Color TextSelected;
    public static Color TextSecondary;
    public static Color TextInactive;
    public static Color Badge;
    public static Color BadgeGlyph;
    public static Color ButtonFill;
    public static Color ButtonFillHover;
    public static Color ButtonFillPressed;
    public static Color ButtonBorder;
    public static Color CaptionHover;
    public static Color CaptionPressed;
    public static Color CloseHover;
    public static Color ClosePressed;
    public static Color Divider;
    public static Color ScrollThumb;
    public static Color ScrollThumbHover;
    public static Color Toast;
    public static Color ToastBorder;
    public static Color FrameBorder;
    public static Color ChipText;
    public static Color ChipBorder;
    public static Color ChipFillHover;
    public static Color ChipSelected;
    public static Color ChipSelectedText;
    public static Color SectionText;
    public static Color SectionCount;
    public static Color Sidebar;
    public static Color NavHover;
    public static Color NavSelected;
    public static Color NavAccent;

    // Extra surfaces that used to be hardcoded at their call sites.
    /// <summary>Opaque ink used for translucent highlights: white on dark, black on light.</summary>
    public static Color Overlay;
    public static Color DropVeil;
    public static Color FocusRing;
    public static Color FocusBorder;
    public static Color SearchFill;
    public static Color InputFill;
    public static Color InputBorder;
    public static Color InputBorderFocus;
    public static Color SecondaryButtonFill;
    public static Color SecondaryButtonHover;
    public static Color SecondaryButtonPressed;
    public static Color SecondaryButtonBorder;
    public static Color PrimaryButtonHover;
    public static Color PrimaryButtonPressed;
    public static Color CatalogTrack;

    private static void ApplyPalette(bool dark)
    {
        // Translucent fills are white ink over dark surfaces, black ink over light surfaces.
        Color ink = dark ? Color.White : Color.Black;
        Color O(int a) => Color.FromArgb(a, ink.R, ink.G, ink.B);

        if (dark)
        {
            Background = Rgb(0x1C1C1C);
            Card = Rgb(0x242424);
            CardHover = Rgb(0x2A2A2A);
            CardPressed = Rgb(0x272727);
            CardSelected = Rgb(0x2A2A2A);
            Border = O(16);
            BorderHover = O(30);
            BorderSelected = Rgb(0xD4D4D4);
            Text = Rgb(0xE8E8E8);
            TextSelected = Rgb(0xFFFFFF);
            TextSecondary = Rgb(0x9A9A9A);
            TextInactive = Rgb(0x777777);
            Badge = Rgb(0xEDEDED);
            BadgeGlyph = Rgb(0x1C1C1C);
            ButtonFill = O(12);
            ButtonFillHover = O(22);
            ButtonFillPressed = O(8);
            ButtonBorder = O(18);
            CaptionHover = O(18);
            CaptionPressed = O(10);
            Divider = O(14);
            ScrollThumb = O(44);
            ScrollThumbHover = O(90);
            Toast = Rgb(0x2E2E2E);
            ToastBorder = O(18);
            FrameBorder = Rgb(0x333333);
            ChipText = Rgb(0xB4B4B4);
            ChipBorder = O(22);
            ChipFillHover = O(16);
            ChipSelected = Rgb(0xE6E6E6);
            ChipSelectedText = Rgb(0x1A1A1A);
            SectionText = Rgb(0xA6A6A6);
            SectionCount = Rgb(0x6A6A6A);
            Sidebar = Rgb(0x191919);
            NavHover = O(10);
            NavSelected = O(20);
            NavAccent = Rgb(0xE6E6E6);

            Overlay = Color.White;
            DropVeil = Rgb(0x161616);
            FocusRing = Color.FromArgb(220, 255, 255, 255);
            FocusBorder = Color.FromArgb(80, 255, 255, 255);
            SearchFill = Rgb(0x262626);
            InputFill = Rgb(0x262626);
            InputBorder = Rgb(0x383838);
            InputBorderFocus = Rgb(0x6A6A6A);
            SecondaryButtonFill = Rgb(0x2D2D2D);
            SecondaryButtonHover = Rgb(0x353535);
            SecondaryButtonPressed = Rgb(0x2A2A2A);
            SecondaryButtonBorder = Rgb(0x3A3A3A);
            PrimaryButtonHover = Rgb(0xF2F2F2);
            PrimaryButtonPressed = Rgb(0xD6D6D6);
            CatalogTrack = Color.FromArgb(46, 255, 255, 255);
        }
        else
        {
            Background = Rgb(0xF9F9F9);
            Card = Rgb(0xFFFFFF);
            CardHover = Rgb(0xF3F3F3);
            CardPressed = Rgb(0xEAEAEA);
            CardSelected = Rgb(0xF0F0F0);
            Border = O(26);
            BorderHover = O(46);
            BorderSelected = Rgb(0x767676);
            Text = Rgb(0x1B1B1B);
            TextSelected = Rgb(0x000000);
            TextSecondary = Rgb(0x5C5C5C);
            TextInactive = Rgb(0x8A8A8A);
            Badge = Rgb(0x2B2B2B);
            BadgeGlyph = Rgb(0xFFFFFF);
            ButtonFill = O(8);
            ButtonFillHover = O(18);
            ButtonFillPressed = O(6);
            ButtonBorder = O(24);
            CaptionHover = O(16);
            CaptionPressed = O(9);
            Divider = O(16);
            ScrollThumb = O(52);
            ScrollThumbHover = O(110);
            Toast = Rgb(0xFFFFFF);
            ToastBorder = O(60);
            FrameBorder = Rgb(0xD6D6D6);
            ChipText = Rgb(0x3F3F3F);
            ChipBorder = O(26);
            ChipFillHover = O(14);
            ChipSelected = Rgb(0x2B2B2B);
            ChipSelectedText = Rgb(0xFFFFFF);
            SectionText = Rgb(0x444444);
            SectionCount = Rgb(0x767676);
            Sidebar = Rgb(0xF1F1F1);
            NavHover = O(12);
            NavSelected = O(24);
            NavAccent = Rgb(0x2B2B2B);

            Overlay = Color.Black;
            DropVeil = Rgb(0xFFFFFF);
            FocusRing = Color.FromArgb(170, 0, 0, 0);
            FocusBorder = Color.FromArgb(90, 0, 0, 0);
            SearchFill = Rgb(0xFFFFFF);
            InputFill = Rgb(0xFFFFFF);
            InputBorder = Rgb(0xD2D2D2);
            InputBorderFocus = Rgb(0x8A8A8A);
            SecondaryButtonFill = Rgb(0xF0F0F0);
            SecondaryButtonHover = Rgb(0xE6E6E6);
            SecondaryButtonPressed = Rgb(0xDADADA);
            SecondaryButtonBorder = Rgb(0xD2D2D2);
            PrimaryButtonHover = Rgb(0x404040);
            PrimaryButtonPressed = Rgb(0x555555);
            CatalogTrack = Color.FromArgb(38, 0, 0, 0);
        }

        // Accent-free close button: same red in both palettes.
        CloseHover = Rgb(0xC42B1C);
        ClosePressed = Rgb(0xA52618);
    }

    public static Color Rgb(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    public static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Max(0, Math.Min(1, t));
        return Color.FromArgb(
            (int)Math.Round(a.A + (b.A - a.A) * t),
            (int)Math.Round(a.R + (b.R - a.R) * t),
            (int)Math.Round(a.G + (b.G - a.G) * t),
            (int)Math.Round(a.B + (b.B - a.B) * t));
    }

    public static Color Fade(Color c, float opacity) =>
        Color.FromArgb((int)Math.Round(c.A * Math.Max(0, Math.Min(1, opacity))), c.R, c.G, c.B);

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static float EaseOut(float t) => 1 - (1 - t) * (1 - t) * (1 - t);
}

/// <summary>Critically damped approach toward a target; interruptible and frame-rate independent.</summary>
internal struct Smooth
{
    public float Value;
    public float Target;

    public bool Active => Value != Target;

    public void Snap(float v) => Value = Target = v;

    public bool Step(float dtMs, float tauMs)
    {
        if (Value == Target) return false;
        float k = 1f - (float)Math.Exp(-dtMs / tauMs);
        Value += (Target - Value) * k;
        if (Math.Abs(Target - Value) < 0.002f) Value = Target;
        return true;
    }
}

/// <summary>Fonts sized in pixels for the current DPI, with graceful fallbacks for Windows 10.</summary>
internal sealed class UiFonts : IDisposable
{
    public Font Title { get; }
    public Font Label { get; }
    public Font Button { get; }
    public Font Chip { get; }
    public Font Section { get; }
    public Font Small { get; }
    public Font Glyphs { get; }

    public UiFonts(float scale, bool chinese = false)
    {
        // Chinese glyphs aren't in Segoe UI Variable; prefer a CJK UI face so text metrics and weight stay consistent.
        string[] text = chinese
            ? new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI Variable Text", "Segoe UI" }
            : new[] { "Segoe UI Variable Text", "Segoe UI" };
        string[] semibold = chinese
            ? new[] { "Microsoft YaHei UI Semibold", "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI Semibold", "Segoe UI" }
            : new[] { "Segoe UI Variable Display Semib", "Segoe UI Semibold", "Segoe UI" };

        Title = Create(16f * scale, FontStyle.Regular, semibold);
        Label = Create(13f * scale, FontStyle.Regular, text);
        Button = Create(13f * scale, FontStyle.Regular, text);
        Chip = Create(12.5f * scale, FontStyle.Regular, text);
        Section = Create(13f * scale, FontStyle.Regular, semibold);
        Small = Create(12f * scale, FontStyle.Regular, text);
        Glyphs = Create(10f * scale, FontStyle.Regular, "Segoe Fluent Icons", "Segoe MDL2 Assets", "Marlett");
    }

    private static Font Create(float px, FontStyle style, params string[] families)
    {
        foreach (string name in families)
        {
            try
            {
                using var family = new FontFamily(name);
                if (family.IsStyleAvailable(style)) return new Font(family, px, style, GraphicsUnit.Pixel);
            }
            catch (ArgumentException) { }
        }
        return new Font(SystemFonts.MessageBoxFont.FontFamily, px, style, GraphicsUnit.Pixel);
    }

    public void Dispose()
    {
        Title.Dispose();
        Label.Dispose();
        Button.Dispose();
        Chip.Dispose();
        Section.Dispose();
        Small.Dispose();
        Glyphs.Dispose();
    }
}
