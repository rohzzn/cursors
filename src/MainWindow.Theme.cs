using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Cursors;

/// <summary>
/// The title-bar theme button, its menu, and live following of the Windows app light/dark setting.
/// The icon shows the current choice: half-filled circle = follow system, sun = light, moon = dark.
/// </summary>
internal sealed partial class MainWindow
{
    private const float ThemeIconWidth = 30, ThemeChevronWidth = 18;
    private Smooth _themeHover, _themePress;

    /// <summary>A compact icon button immediately to the left of the language button.</summary>
    private Rectangle ThemeButtonRect()
    {
        float s = S;
        int w = (int)Math.Round((ThemeIconWidth + ThemeChevronWidth + 8) * s);
        int h = (int)Math.Round(ButtonHeight * s);
        int x = LanguageButtonRect().X - (int)Math.Round(8 * s) - w;
        return new Rectangle(x, (TitleBarPx - h) / 2, w, h);
    }

    private void DrawThemeButton(Graphics g)
    {
        var rb = ThemeButtonRect();
        float s = S, hover = _themeHover.Value, press = _themePress.Value;
        var fill = Theme.Lerp(Theme.Lerp(Theme.ButtonFill, Theme.ButtonFillHover, hover), Theme.ButtonFillPressed, press);
        var behind = OpaqueOver(Theme.Background, fill);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.None;

        using (var path = Theme.RoundedRect(new RectangleF(rb.X, rb.Y, rb.Width, rb.Height), 6 * s))
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);
        using (var path = Theme.RoundedRect(new RectangleF(rb.X + 0.5f, rb.Y + 0.5f, rb.Width - 1, rb.Height - 1), 6 * s))
        using (var pen = new Pen(Theme.ButtonBorder, 1f))
            g.DrawPath(pen, path);

        var iconColor = Theme.Lerp(_windowActive ? Theme.Text : Theme.TextSecondary, Theme.Text, hover * 0.5f);
        DrawThemeIcon(g, rb, s, iconColor, behind);

        int chevW = (int)Math.Round(ThemeChevronWidth * s);
        TextRenderer.DrawText(g, "\uE70D", _fonts.Glyphs,
            new Rectangle(rb.Right - chevW - (int)Math.Round(2 * s), rb.Y, chevW, rb.Height), Theme.TextSecondary, behind,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    private void DrawThemeIcon(Graphics g, Rectangle r, float s, Color color, Color behind)
    {
        float d = 14 * s;
        float cx = r.X + ThemeIconWidth * s / 2f, cy = r.Y + r.Height / 2f;
        var box = new RectangleF(cx - d / 2, cy - d / 2, d, d);
        using var pen = new Pen(color, 1.2f * s);
        using var brush = new SolidBrush(color);

        if (Theme.Selection == AppTheme.Auto)
        {
            // Half-filled circle: the app and Windows each own one half.
            g.DrawEllipse(pen, box);
            using var half = new GraphicsPath();
            half.AddArc(box.X, box.Y, box.Width, box.Height, 90, 180);
            half.CloseFigure();
            g.FillPath(brush, half);
        }
        else if (Theme.IsDark)
        {
            // Crescent: a filled disc with the button's own background punched out of it.
            g.FillEllipse(brush, box);
            using var cut = new SolidBrush(behind);
            g.FillEllipse(cut, box.X + d * 0.34f, box.Y - d * 0.18f, d * 1.02f, d * 1.02f);
        }
        else
        {
            float inner = d * 0.28f;
            g.DrawEllipse(pen, cx - inner, cy - inner, inner * 2, inner * 2);
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                float c = (float)Math.Cos(a), sn = (float)Math.Sin(a);
                g.DrawLine(pen, cx + c * d * 0.43f, cy + sn * d * 0.43f, cx + c * d * 0.62f, cy + sn * d * 0.62f);
            }
        }
    }

    private void ShowThemeMenu()
    {
        var r = ThemeButtonRect();
        int command = ShowMenu(new Point(r.X, r.Bottom + (int)Math.Round(4 * S)),
            (1, L.S.ThemeAuto, Theme.Selection == AppTheme.Auto, true),
            (2, L.S.ThemeLight, Theme.Selection == AppTheme.Light, true),
            (3, L.S.ThemeDark, Theme.Selection == AppTheme.Dark, true));
        AppTheme chosen = command switch
        {
            1 => AppTheme.Auto,
            2 => AppTheme.Light,
            3 => AppTheme.Dark,
            _ => Theme.Selection,
        };
        if (chosen == Theme.Selection) return;
        ApplyTheme(chosen);
        ShowToast(chosen == AppTheme.Auto ? L.S.ThemeAutoToast
            : Theme.IsDark ? L.S.ThemeDarkToast : L.S.ThemeLightToast);
    }

    private void ApplyTheme(AppTheme theme)
    {
        Theme.Set(theme);
        _settings["theme"] = Theme.StoreValue;
        IniFile.Write(AppPaths.SettingsFile, _settings);
        ApplyThemeVisual();
    }

    /// <summary>Re-applies native chrome and refreshes every drawn surface after a palette change.</summary>
    private void ApplyThemeVisual()
    {
        Native.SetMenuTheme(Theme.IsDark);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        if (_search != null)
        {
            _search.BackColor = Theme.SearchFill;
            _search.ForeColor = Theme.Text;
        }
        if (IsHandleCreated)
        {
            Native.ApplyFrame(Handle, Theme.IsDark, Native.ToColorRef(Theme.FrameBorder), Native.ToColorRef(Theme.Background));
            Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                Native.SWP_FRAMECHANGED | Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }
        Invalidate();
        StartAnimation();
    }

    /// <summary>Called from WM_SETTINGCHANGE; only acts when Windows actually flipped light/dark.</summary>
    private void RefreshSystemTheme()
    {
        if (Theme.Selection != AppTheme.Auto) return;
        if (!Theme.Refresh()) return;
        ApplyThemeVisual();
    }
}
