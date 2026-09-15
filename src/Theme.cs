using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Cursors;

internal static class Theme
{
    public static readonly Color Background = Rgb(0x1C1C1C);
    public static readonly Color Card = Rgb(0x242424);
    public static readonly Color CardHover = Rgb(0x2A2A2A);
    public static readonly Color CardPressed = Rgb(0x272727);
    public static readonly Color CardSelected = Rgb(0x2A2A2A);
    public static readonly Color Border = Color.FromArgb(16, 255, 255, 255);
    public static readonly Color BorderHover = Color.FromArgb(30, 255, 255, 255);
    public static readonly Color BorderSelected = Rgb(0xD4D4D4);
    public static readonly Color Text = Rgb(0xE8E8E8);
    public static readonly Color TextSelected = Rgb(0xFFFFFF);
    public static readonly Color TextSecondary = Rgb(0x9A9A9A);
    public static readonly Color TextInactive = Rgb(0x777777);
    public static readonly Color Badge = Rgb(0xEDEDED);
    public static readonly Color BadgeGlyph = Rgb(0x1C1C1C);
    public static readonly Color ButtonFill = Color.FromArgb(12, 255, 255, 255);
    public static readonly Color ButtonFillHover = Color.FromArgb(22, 255, 255, 255);
    public static readonly Color ButtonFillPressed = Color.FromArgb(8, 255, 255, 255);
    public static readonly Color ButtonBorder = Color.FromArgb(18, 255, 255, 255);
    public static readonly Color CaptionHover = Color.FromArgb(18, 255, 255, 255);
    public static readonly Color CaptionPressed = Color.FromArgb(10, 255, 255, 255);
    public static readonly Color CloseHover = Rgb(0xC42B1C);
    public static readonly Color ClosePressed = Rgb(0xA52618);
    public static readonly Color Divider = Color.FromArgb(14, 255, 255, 255);
    public static readonly Color ScrollThumb = Color.FromArgb(44, 255, 255, 255);
    public static readonly Color ScrollThumbHover = Color.FromArgb(90, 255, 255, 255);
    public static readonly Color Toast = Rgb(0x2E2E2E);
    public static readonly Color FrameBorder = Rgb(0x333333);
    public static readonly Color ChipText = Rgb(0xB4B4B4);
    public static readonly Color ChipBorder = Color.FromArgb(22, 255, 255, 255);
    public static readonly Color ChipFillHover = Color.FromArgb(16, 255, 255, 255);
    public static readonly Color ChipSelected = Rgb(0xE6E6E6);
    public static readonly Color ChipSelectedText = Rgb(0x1A1A1A);
    public static readonly Color SectionText = Rgb(0xA6A6A6);
    public static readonly Color SectionCount = Rgb(0x6A6A6A);
    public static readonly Color Sidebar = Rgb(0x191919);
    public static readonly Color NavHover = Color.FromArgb(10, 255, 255, 255);
    public static readonly Color NavSelected = Color.FromArgb(20, 255, 255, 255);
    public static readonly Color NavAccent = Rgb(0xE6E6E6);

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

    public UiFonts(float scale)
    {
        Title = Create(16f * scale, FontStyle.Regular, "Segoe UI Variable Display Semib", "Segoe UI Semibold", "Segoe UI");
        Label = Create(13f * scale, FontStyle.Regular, "Segoe UI Variable Text", "Segoe UI");
        Button = Create(13f * scale, FontStyle.Regular, "Segoe UI Variable Text", "Segoe UI");
        Chip = Create(12.5f * scale, FontStyle.Regular, "Segoe UI Variable Text", "Segoe UI");
        Section = Create(13f * scale, FontStyle.Regular, "Segoe UI Variable Text Semibold", "Segoe UI Semibold", "Segoe UI");
        Small = Create(12f * scale, FontStyle.Regular, "Segoe UI Variable Text", "Segoe UI");
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
