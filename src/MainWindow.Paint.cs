using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace Cursors;

internal sealed partial class MainWindow
{
    private const string RestoreLabel = "Restore default";
    private const TextFormatFlags CenteredText = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
        TextFormatFlags.PreserveGraphicsClipping;

    private int _restoreTextWidth = -1;

    private Rectangle RestoreButtonRect()
    {
        if (_restoreTextWidth < 0)
            _restoreTextWidth = TextRenderer.MeasureText(RestoreLabel, _fonts.Button, Size.Empty, TextFormatFlags.NoPadding).Width;
        float s = S;
        int w = _restoreTextWidth + (int)Math.Round(28 * s);
        int h = (int)Math.Round(ButtonHeight * s);
        int x = CaptionButtonRect(Native.HTMINBUTTON).X - (int)Math.Round(12 * s) - w;
        return new Rectangle(x, (TitleBarPx - h) / 2, w, h);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var clip = e.ClipRectangle;
        using (var bg = new SolidBrush(Theme.Background)) g.FillRectangle(bg, clip);

        int top = ViewportTopPx, side = SidebarPx;
        if (clip.Bottom > top && clip.Right > side)
        {
            var state = g.Save();
            g.SetClip(new Rectangle(side, top, ClientSize.Width - side, ClientSize.Height - top), CombineMode.Intersect);
            long now = _clock.ElapsedMilliseconds / AnimationFrameMs * AnimationFrameMs;
            foreach (var section in _sections) DrawSection(g, section, clip);
            VisibleRange(8 * S, out int first, out int last);
            for (int i = first; i <= last; i++)
            {
                var r = ScreenRect(_cards[i]);
                if (r.Bottom < top - 8 || r.Top > ClientSize.Height) continue;
                if (!Rectangle.Round(RectangleF.Inflate(r, 6 * S, 6 * S)).IntersectsWith(clip)) continue;
                if (_cards[i].IsAdd) DrawAddCard(g, _cards[i], r, i);
                else DrawCard(g, _cards[i], r, i, now);
            }
            RequestVisibleCatalogPreviews();
            if (_cards.Count == 0) DrawEmptyState(g);
            g.Restore(state);
            DrawScrollbar(g);
        }

        if (clip.Top < top)
        {
            DrawTitleBar(g);
            if (clip.Right > side) DrawHeader(g);
        }
        if (clip.Left < side && clip.Bottom > TitleBarPx) DrawSidebar(g);
        if (_dropHover.Value > 0) DrawDropOverlay(g);
        if (_toastText != null && _toastAnim.Value > 0) DrawToast(g);
    }

    private void DrawCard(Graphics g, Card card, RectangleF r, int index, long now)
    {
        float s = S;
        float hover = card.Hover.Value, press = card.Press.Value, selected = card.Selected.Value;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.None;

        var fill = Theme.Lerp(Theme.Card, Theme.CardSelected, selected);
        fill = Theme.Lerp(fill, Theme.CardHover, hover);
        fill = Theme.Lerp(fill, Theme.CardPressed, press);
        using (var path = Theme.RoundedRect(r, CardRadius * s))
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);

        float bw = Math.Max(1f, (float)Math.Round(s)) * (1f + 0.5f * selected);
        var border = Theme.Lerp(Theme.Lerp(Theme.Border, Theme.BorderHover, hover), Theme.BorderSelected, selected);
        using (var path = Theme.RoundedRect(RectangleF.Inflate(r, -bw / 2, -bw / 2), CardRadius * s - bw / 2))
        using (var pen = new Pen(border, bw))
            g.DrawPath(pen, path);

        if (_keyboardFocus && index == _focusIndex && ContainsFocus) DrawFocusRing(g, r);

        float labelHeight = 20 * s, bottomPad = 16 * s;
        float areaH = r.Height - labelHeight - bottomPad;
        float peek = card.Pack.RolePreviews != null ? Theme.EaseOut(card.Peek.Value) : 0;
        var preview = card.Pack.Preview;
        if (preview != null)
        {
            var frame = index == _hoverIndex && preview.IsAnimated
                ? preview.FrameAt(now - card.HoverStart)
                : preview.Frames[preview.Sequence[0]];
            float scale = (1f + 0.045f * Theme.EaseOut(hover) - 0.05f * press) * (1f - 0.22f * peek);
            float appear = card.Appear.Value;
            float cx = r.X + r.Width / 2, cy = r.Y + areaH / 2 + 6 * s - 14 * s * peek;
            float w = frame.Width * scale, h = frame.Height * scale;
            float y = cy - h / 2 - 2 * s * hover * (1 - peek) + (1 - Theme.EaseOut(appear)) * 6 * s;

            bool exact = scale == 1f && hover == 0f && appear >= 1f && peek == 0f;
            if (exact)
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(frame, new Rectangle((int)Math.Round(cx - w / 2), (int)Math.Round(y), frame.Width, frame.Height));
            }
            else
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                var dest = new RectangleF(cx - w / 2, y, w, h);
                if (appear < 1f)
                {
                    using var attrs = new ImageAttributes();
                    attrs.SetColorMatrix(new ColorMatrix { Matrix33 = Theme.EaseOut(appear) });
                    g.DrawImage(frame, Rectangle.Round(dest), 0, 0, frame.Width, frame.Height, GraphicsUnit.Pixel, attrs);
                }
                else
                {
                    g.DrawImage(frame, dest);
                }
            }
            g.PixelOffsetMode = PixelOffsetMode.None;
        }
        if (peek > 0.01f) DrawPeek(g, card, r, areaH, peek, index == _hoverIndex, now);

        var labelRect = Rectangle.Round(new RectangleF(r.X + 12 * s, r.Bottom - bottomPad - labelHeight, r.Width - 24 * s, labelHeight));
        var textColor = Theme.Lerp(Theme.Text, Theme.TextSelected, Math.Max(selected, hover * 0.6f));
        TextRenderer.DrawText(g, card.Pack.Label, _fonts.Label, labelRect, textColor, CenteredText);

        if (selected > 0.01f) DrawBadge(g, r, selected);
        if (card.Pack.Kind == PackKind.Catalog) DrawCatalogExtras(g, card, r, areaH, now);
    }

    /// <summary>A row of the pack's link, text and busy cursors that fades in under the pointer on hover.</summary>
    private void DrawPeek(Graphics g, Card card, RectangleF r, float areaH, float peek, bool hovered, long now)
    {
        float s = S, spacing = 30 * s;
        var previews = card.Pack.RolePreviews;
        int count = 0;
        foreach (var p in previews)
            if (p != null) count++;
        if (count == 0) return;

        float cx = r.X + r.Width / 2, rowY = r.Y + areaH / 2 + 36 * s + (1 - peek) * 6 * s;
        using var attrs = new ImageAttributes();
        attrs.SetColorMatrix(new ColorMatrix { Matrix33 = peek * 0.92f });
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        int slot = 0;
        foreach (var p in previews)
        {
            if (p == null) continue;
            var frame = hovered && p.IsAnimated ? p.FrameAt(now - card.HoverStart) : p.Frames[p.Sequence[0]];
            float x = cx + (slot - (count - 1) / 2f) * spacing;
            var dest = new Rectangle((int)Math.Round(x - frame.Width / 2f), (int)Math.Round(rowY - frame.Height / 2f), frame.Width, frame.Height);
            g.DrawImage(frame, dest, 0, 0, frame.Width, frame.Height, GraphicsUnit.Pixel, attrs);
            slot++;
        }
        g.PixelOffsetMode = PixelOffsetMode.None;
    }

    private void DrawBadge(Graphics g, RectangleF card, float t)
    {
        float s = S, e = Theme.EaseOut(t);
        float d = 18 * s * (0.7f + 0.3f * e);
        float cx = card.Right - 10 * s - 9 * s, cy = card.Y + 10 * s + 9 * s;
        var circle = new RectangleF(cx - d / 2, cy - d / 2, d, d);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(Theme.Fade(Theme.Badge, t))) g.FillEllipse(brush, circle);
        using var pen = new Pen(Theme.Fade(Theme.BadgeGlyph, t), 1.6f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, new[]
        {
            new PointF(circle.X + d * 0.29f, circle.Y + d * 0.52f),
            new PointF(circle.X + d * 0.44f, circle.Y + d * 0.66f),
            new PointF(circle.X + d * 0.72f, circle.Y + d * 0.37f),
        });
    }

    private void DrawFocusRing(Graphics g, RectangleF r)
    {
        float s = S;
        var ring = RectangleF.Inflate(r, 3 * s, 3 * s);
        using var path = Theme.RoundedRect(ring, CardRadius * s + 3 * s);
        using var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 2 * s);
        g.DrawPath(pen, path);
    }

    private void DrawAddCard(Graphics g, Card card, RectangleF r, int index)
    {
        float s = S, hover = card.Hover.Value, press = card.Press.Value;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.None;

        using (var path = Theme.RoundedRect(r, CardRadius * s))
        using (var brush = new SolidBrush(Color.FromArgb(Math.Max(0, (int)(10 * hover - 4 * press)), 255, 255, 255)))
            g.FillPath(brush, path);

        using (var path = Theme.RoundedRect(RectangleF.Inflate(r, -0.5f * s, -0.5f * s), CardRadius * s))
        using (var pen = new Pen(Color.FromArgb((int)(34 + 30 * hover), 255, 255, 255), Math.Max(1f, (float)Math.Round(s))) { DashPattern = new[] { 4f, 3f } })
            g.DrawPath(pen, path);

        if (_keyboardFocus && index == _focusIndex && ContainsFocus) DrawFocusRing(g, r);

        float labelHeight = 20 * s, bottomPad = 16 * s;
        float cx = r.X + r.Width / 2, cy = r.Y + (r.Height - labelHeight - bottomPad) / 2 + 6 * s;
        float arm = 10 * s * (1 - 0.08f * press);
        var color = Theme.Lerp(Theme.TextSecondary, Theme.Text, hover);
        using (var pen = new Pen(color, 1.5f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(pen, cx - arm, cy, cx + arm, cy);
            g.DrawLine(pen, cx, cy - arm, cx, cy + arm);
        }

        var labelRect = Rectangle.Round(new RectangleF(r.X + 12 * s, r.Bottom - bottomPad - labelHeight, r.Width - 24 * s, labelHeight));
        TextRenderer.DrawText(g, "Add pack", _fonts.Label, labelRect, color, CenteredText);
    }

    private void DrawSection(Graphics g, Section section, Rectangle clip)
    {
        var r = ScreenRect(section.Bounds);
        if (r.Bottom < ViewportTop || r.Top > ClientSize.Height || !Rectangle.Round(r).IntersectsWith(clip)) return;
        float s = S;
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.SingleLine |
                                      TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping;
        var titleRect = Rectangle.Round(new RectangleF(r.X + 2 * s, r.Y, r.Width - 4 * s, r.Height - 12 * s));
        TextRenderer.DrawText(g, section.Title, _fonts.Section, titleRect, Theme.SectionText, flags);
        if (section.Count <= 0) return;
        int titleWidth = TextRenderer.MeasureText(section.Title, _fonts.Section, Size.Empty, TextFormatFlags.NoPadding).Width;
        var countRect = new Rectangle(titleRect.X + titleWidth + (int)Math.Round(8 * s), titleRect.Y, (int)Math.Round(60 * s), titleRect.Height);
        TextRenderer.DrawText(g, section.Count.ToString(CultureInfo.CurrentCulture), _fonts.Chip, countRect, Theme.SectionCount, flags);
    }



    private void DrawTitleBar(Graphics g)
    {
        float s = S;
        int barH = TitleBarPx;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.None;
        using (var bg = new SolidBrush(Theme.Background)) g.FillRectangle(bg, 0, 0, ClientSize.Width, barH);

        var titleColor = _windowActive ? Theme.Text : Theme.TextInactive;
        TextRenderer.DrawText(g, "Cursors", _fonts.Title,
            new Rectangle((int)Math.Round(PadX * s), 0, (int)(240 * s), barH), titleColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        var rb = RestoreButtonRect();
        float rh = _restoreHover.Value, rp = _restorePress.Value;
        var btnFill = Theme.Lerp(Theme.Lerp(Theme.ButtonFill, Theme.ButtonFillHover, rh), Theme.ButtonFillPressed, rp);
        using (var path = Theme.RoundedRect(new RectangleF(rb.X, rb.Y, rb.Width, rb.Height), 6 * s))
        using (var brush = new SolidBrush(btnFill))
            g.FillPath(brush, path);
        using (var path = Theme.RoundedRect(new RectangleF(rb.X + 0.5f, rb.Y + 0.5f, rb.Width - 1, rb.Height - 1), 6 * s))
        using (var pen = new Pen(Theme.ButtonBorder, 1f))
            g.DrawPath(pen, path);
        var btnText = Theme.Lerp(_windowActive ? Theme.Text : Theme.TextSecondary, Theme.TextSecondary, rp * 0.6f);
        TextRenderer.DrawText(g, RestoreLabel, _fonts.Button, rb, btnText, CenteredText & ~TextFormatFlags.PreserveGraphicsClipping);
        DrawSearchBox(g);

        DrawCaptionButton(g, Native.HTMINBUTTON, "\uE921", _minHover.Value, false);
        DrawCaptionButton(g, Native.HTMAXBUTTON, WindowState == FormWindowState.Maximized ? "\uE923" : "\uE922", _maxHover.Value, false);
        DrawCaptionButton(g, Native.HTCLOSE, "\uE8BB", _closeHover.Value, true);
    }

    private void DrawCaptionButton(Graphics g, int ht, string glyph, float hover, bool isClose)
    {
        var rect = CaptionButtonRect(ht);
        bool pressed = _captionPressed == ht && _captionHover == ht;
        Color fill = isClose
            ? (pressed ? Theme.ClosePressed : Theme.Fade(Theme.CloseHover, hover))
            : (pressed ? Theme.CaptionPressed : Theme.Fade(Theme.CaptionHover, hover));
        if (fill.A > 0)
            using (var brush = new SolidBrush(fill)) g.FillRectangle(brush, rect);

        var baseColor = _windowActive ? Theme.Text : Theme.TextInactive;
        var color = isClose ? Theme.Lerp(baseColor, Color.White, pressed ? 1 : hover) : baseColor;
        // GDI text ignores alpha, so blend the glyph against what's behind it.
        var behind = isClose && fill.A > 0 ? Theme.Lerp(Theme.Background, Theme.CloseHover, fill.A / 255f) : Theme.Background;
        TextRenderer.DrawText(g, glyph, _fonts.Glyphs, rect, color, behind,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    private Rectangle ScrollTrackRect()
    {
        float s = S;
        return Rectangle.Round(new RectangleF(ClientSize.Width - 14 * s, ViewportTop + 4 * s, 14 * s, ViewportHeight - 8 * s));
    }

    private RectangleF ScrollThumbRect()
    {
        float s = S;
        var track = ScrollTrackRect();
        float thumbH = Math.Max(36 * s, track.Height * ViewportHeight / Math.Max(1, _contentHeight));
        float y = track.Y + (track.Height - thumbH) * (MaxScroll > 0 ? _scroll.Value / MaxScroll : 0);
        float w = (3 + 3 * _scrollbarHover.Value) * s;
        return new RectangleF(ClientSize.Width - 4 * s - w, y, w, thumbH);
    }

    private void DrawScrollbar(Graphics g)
    {
        if (!HasScroll) return;
        var thumb = ScrollThumbRect();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedRect(thumb, thumb.Width / 2);
        using var brush = new SolidBrush(Theme.Lerp(Theme.ScrollThumb, Theme.ScrollThumbHover, _scrollbarHover.Value));
        g.FillPath(brush, path);
    }

    private void DrawDropOverlay(Graphics g)
    {
        float s = S, t = _dropHover.Value;
        var area = new RectangleF(SidebarPx + 12 * s, ViewportTop + 4 * s, ClientSize.Width - SidebarPx - 24 * s, ClientSize.Height - ViewportTop - 16 * s);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.RoundedRect(area, 14 * s))
        {
            using (var brush = new SolidBrush(Color.FromArgb((int)(200 * t), 22, 22, 22))) g.FillPath(brush, path);
            using (var pen = new Pen(Color.FromArgb((int)(110 * t), 255, 255, 255), 1.5f * s) { DashPattern = new[] { 5f, 4f } }) g.DrawPath(pen, path);
        }
        DrawAlphaText(g, "Drop to add cursor packs", _fonts.Title, area, Theme.Fade(Theme.Text, t));
    }

    private Rectangle ToastRect()
    {
        float s = S;
        int textW = _toastText == null ? 0 : TextRenderer.MeasureText(_toastText, _fonts.Label, Size.Empty, TextFormatFlags.NoPadding).Width;
        int w = textW + (int)(40 * s), h = (int)(36 * s);
        return new Rectangle(SidebarPx + (ClientSize.Width - SidebarPx - w) / 2, ClientSize.Height - (int)(24 * s) - h, w, h);
    }

    private void DrawToast(Graphics g)
    {
        float s = S, t = _toastAnim.Value;
        var rect = ToastRect();
        var r = new RectangleF(rect.X, rect.Y + (1 - Theme.EaseOut(t)) * 10 * s, rect.Width, rect.Height);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.RoundedRect(r, r.Height / 2))
        {
            using (var brush = new SolidBrush(Theme.Fade(Theme.Toast, t))) g.FillPath(brush, path);
            using (var pen = new Pen(Theme.Fade(Theme.ButtonBorder, t), 1f)) g.DrawPath(pen, path);
        }
        DrawAlphaText(g, _toastText, _fonts.Label, r, Theme.Fade(Theme.Text, t));
    }

    private static void DrawAlphaText(Graphics g, string text, Font font, RectangleF area, Color color)
    {
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
        };
        g.DrawString(text, font, brush, area, format);
        g.TextRenderingHint = TextRenderingHint.SystemDefault;
    }

    private void ShowToast(string text)
    {
        if (_toastText != null) InvalidateToast();
        _toastText = text;
        _toastUntil = _clock.ElapsedMilliseconds + 2600;
        _toastAnim.Target = 1;
        InvalidateToast();
        StartAnimation();
    }

    private void InvalidateToast()
    {
        var r = ToastRect();
        r.Inflate(r.Width / 2 + 40, (int)(14 * S));
        Invalidate(r);
    }

    private string _toastText;
    private long _toastUntil;
    private Smooth _toastAnim, _dropHover, _restoreHover, _restorePress, _scrollbarHover;
}
