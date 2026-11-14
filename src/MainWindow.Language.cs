using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Cursors;

/// <summary>
/// The title-bar language button and its menu. Switching languages rebuilds fonts and text metrics so
/// every drawn label, the search cue and the sidebar update immediately, without restarting the app.
/// </summary>
internal sealed partial class MainWindow
{
    private const float LanguageIconWidth = 30, LanguageChevronWidth = 22;
    private Smooth _languageHover, _languagePress;

    private string LanguageShortName => L.Selection switch
    {
        AppLanguage.Chinese => L.S.LangChineseShort,
        AppLanguage.English => L.S.LangEnglishShort,
        _ => L.S.LangAutoShort,
    };

    /// <summary>A compact button to the left of Restore default; the search box stops before it.</summary>
    private Rectangle LanguageButtonRect()
    {
        float s = S;
        int w = (int)Math.Round(LanguageIconWidth * s) + TextWidth(LanguageShortName, _fonts.Button) +
                (int)Math.Round((LanguageChevronWidth + 10) * s);
        int h = (int)Math.Round(ButtonHeight * s);
        int x = RestoreButtonRect().X - (int)Math.Round(8 * s) - w;
        return new Rectangle(x, (TitleBarPx - h) / 2, w, h);
    }

    private void DrawLanguageButton(Graphics g)
    {
        var rb = LanguageButtonRect();
        float s = S, hover = _languageHover.Value, press = _languagePress.Value;
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

        // A small globe, drawn directly so it doesn't depend on an icon font being installed.
        float d = 13 * s, cx = rb.X + LanguageIconWidth * s / 2, cy = rb.Y + rb.Height / 2f;
        using (var pen = new Pen(Theme.Lerp(Theme.TextSecondary, Theme.Text, hover), 1.2f * s))
        {
            g.DrawEllipse(pen, cx - d / 2, cy - d / 2, d, d);
            g.DrawLine(pen, cx - d / 2, cy, cx + d / 2, cy);
            g.DrawEllipse(pen, cx - d / 4, cy - d / 2, d / 2, d);
        }

        int iconW = (int)Math.Round(LanguageIconWidth * s), chevW = (int)Math.Round(LanguageChevronWidth * s);
        var textColor = Theme.Lerp(_windowActive ? Theme.Text : Theme.TextSecondary, Theme.TextSecondary, press * 0.6f);
        TextRenderer.DrawText(g, LanguageShortName, _fonts.Button,
            new Rectangle(rb.X + iconW, rb.Y, rb.Width - iconW - chevW, rb.Height), textColor, behind,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "\uE70D", _fonts.Glyphs,
            new Rectangle(rb.Right - chevW, rb.Y, chevW, rb.Height), Theme.TextSecondary, behind,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    private void ShowLanguageMenu()
    {
        var r = LanguageButtonRect();
        int command = ShowMenu(new Point(r.X, r.Bottom + (int)Math.Round(4 * S)),
            (1, L.S.LangAuto, L.Selection == AppLanguage.Auto, true),
            (2, L.S.LangChinese, L.Selection == AppLanguage.Chinese, true),
            (3, L.S.LangEnglish, L.Selection == AppLanguage.English, true));
        AppLanguage chosen = command switch
        {
            1 => AppLanguage.Auto,
            2 => AppLanguage.Chinese,
            3 => AppLanguage.English,
            _ => L.Selection,
        };
        if (chosen == L.Selection) return;
        ApplyLanguage(chosen);
        ShowToast(chosen == AppLanguage.Auto
            ? L.S.LangAutoToast
            : (L.IsChinese ? L.S.LangChangedChinese : L.S.LangChangedEnglish));
    }

    private void ApplyLanguage(AppLanguage language)
    {
        L.Set(language);
        _settings["language"] = L.StoreValue;
        IniFile.Write(AppPaths.SettingsFile, _settings);

        var oldFonts = _fonts;
        _fonts = new UiFonts(S, L.IsChinese);
        if (_search != null)
        {
            _search.Font = _fonts.Label;
            RefreshSearchCue();
        }
        oldFonts.Dispose();

        Text = L.S.AppTitle;
        _textWidths.Clear();
        _restoreTextWidth = -1;
        _titleTextWidth = -1;
        RefreshNavLabels();
        LayoutCards(); // also re-lays out the search box and rebuilds section headings
        Invalidate();
        StartAnimation();
    }
}
