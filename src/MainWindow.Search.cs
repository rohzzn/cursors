using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Cursors;

/// <summary>Title-bar search box that filters the library by pack name, style or author as you type.</summary>
internal sealed partial class MainWindow
{
    private const float SearchMaxWidth = 240, SearchMinWidth = 120, SearchHeight = 32;
    private const int EM_SETCUEBANNER = 0x1501;
    private static readonly Color SearchFill = Theme.Rgb(0x262626);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam);

    private TextBox _search;
    private string _query = "";
    private int _titleTextWidth = -1;
    private Smooth _searchFocus, _searchClearHover;

    private void InitSearch()
    {
        // A real edit control keeps caret, selection, IME and clipboard behaviour native; the frame around it is drawn.
        _search = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = SearchFill,
            ForeColor = Theme.Text,
            Font = _fonts.Label,
            TabStop = false,
        };
        _search.HandleCreated += (_, _) => SendMessage(_search.Handle, EM_SETCUEBANNER, (IntPtr)1, "Search packs");
        _search.TextChanged += (_, _) => OnSearchChanged();
        _search.GotFocus += (_, _) =>
        {
            _searchFocus.Target = 1;
            if (_keyboardFocus) InvalidateGrid();
            _keyboardFocus = false;
            StartAnimation();
        };
        _search.LostFocus += (_, _) =>
        {
            _searchFocus.Target = 0;
            StartAnimation();
        };
        Controls.Add(_search);
    }

    private int TitleTextWidth()
    {
        if (_titleTextWidth < 0)
            _titleTextWidth = TextRenderer.MeasureText("Cursors", _fonts.Title, Size.Empty, TextFormatFlags.NoPadding).Width;
        return _titleTextWidth;
    }

    /// <summary>Right-aligned next to Restore default; shrinks on narrow windows and hides below its minimum width.</summary>
    private Rectangle SearchRect()
    {
        float s = S;
        int h = (int)Math.Round(SearchHeight * s);
        int left = (int)Math.Round(PadX * s) + TitleTextWidth() + (int)Math.Round(20 * s);
        int right = RestoreButtonRect().X - (int)Math.Round(8 * s);
        int w = Math.Min((int)Math.Round(SearchMaxWidth * s), right - left);
        return w < SearchMinWidth * s ? Rectangle.Empty : new Rectangle(right - w, (TitleBarPx - h) / 2, w, h);
    }

    private Rectangle SearchClearRect()
    {
        var r = SearchRect();
        int w = (int)Math.Round(30 * S);
        return r.IsEmpty ? Rectangle.Empty : new Rectangle(r.Right - w, r.Y, w, r.Height);
    }

    private void LayoutSearch()
    {
        if (_search == null) return;
        var r = SearchRect();
        _search.Visible = !r.IsEmpty;
        if (r.IsEmpty) return;
        int left = (int)Math.Round(32 * S), right = (int)Math.Round(30 * S);
        int height = _search.PreferredHeight;
        _search.Bounds = new Rectangle(r.X + left, r.Y + (r.Height - height) / 2, Math.Max(10, r.Width - left - right), height);
    }

    private void DrawSearchBox(Graphics g)
    {
        var r = SearchRect();
        if (r.IsEmpty) return;
        float s = S, focus = _searchFocus.Value;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.None;
        using (var path = Theme.RoundedRect(r, 6 * s))
        using (var brush = new SolidBrush(SearchFill))
            g.FillPath(brush, path);
        using (var path = Theme.RoundedRect(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 6 * s))
        using (var pen = new Pen(Theme.Lerp(Theme.ButtonBorder, Color.FromArgb(80, 255, 255, 255), focus), 1f))
            g.DrawPath(pen, path);

        const TextFormatFlags glyph = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        var iconColor = Theme.Lerp(Theme.TextSecondary, Theme.Text, focus);
        TextRenderer.DrawText(g, "", _fonts.Glyphs, new Rectangle(r.X, r.Y, (int)Math.Round(32 * s), r.Height), iconColor, SearchFill, glyph);
        if (_query.Length > 0)
            TextRenderer.DrawText(g, "", _fonts.Glyphs, SearchClearRect(),
                Theme.Lerp(Theme.TextSecondary, Theme.Text, _searchClearHover.Value), SearchFill, glyph);
    }

    private bool StepSearch(float dt)
    {
        if (!(_searchFocus.Step(dt, 45) | _searchClearHover.Step(dt, 45))) return false;
        var r = SearchRect();
        r.Inflate(2, 2);
        Invalidate(r);
        return true;
    }

    private void OnSearchChanged()
    {
        string query = _search.Text.Trim();
        if (query == _query) return;
        _query = query;
        _scroll.Snap(0);
        ApplyFilter(animate: false);
    }

    /// <summary>Every word must appear in the name, style or author; case and accents are ignored.</summary>
    private bool MatchesSearch(Card card)
    {
        if (_query.Length == 0) return true;
        if (card.IsAdd) return false;
        var pack = card.Pack;
        string text = pack.Label + " " + pack.Category + " " + pack.Author + " " + pack.SchemeName;
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        foreach (string word in _query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            if (compare.IndexOf(text, word, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) < 0) return false;
        return true;
    }

    private void FocusSearch()
    {
        if (_search == null || !_search.Visible) return;
        _search.Focus();
        _search.SelectAll();
    }

    private void LeaveSearch()
    {
        ActiveControl = null;
        Focus();
    }

    /// <summary>Search shortcuts, and keeps arrow/Home/End/Space for the text box while it has focus.</summary>
    private bool ProcessSearchKey(ref Message msg, Keys keyData, out bool handled)
    {
        handled = false;
        if (_search == null) return false;
        if (keyData == (Keys.Control | Keys.F))
        {
            FocusSearch();
            return handled = true;
        }
        if (!_search.Focused) return false;
        switch (keyData)
        {
            case Keys.Escape:
                if (_search.TextLength > 0) _search.Clear();
                else LeaveSearch();
                return handled = true;
            case Keys.Enter:
            case Keys.Down:
                if (_cards.Count > 0)
                {
                    LeaveSearch();
                    SetFocus(0);
                }
                return handled = true;
            case Keys.Control | Keys.Tab:
            case Keys.Control | Keys.Shift | Keys.Tab:
            case Keys.Control | Keys.O:
            case Keys.F5:
                return false;
            default:
                return true; // let the text box have it
        }
    }

    /// <summary>Typing anywhere in the window starts a search.</summary>
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (e.Handled || _search == null || _search.Focused || !_search.Visible) return;
        if (char.IsControl(e.KeyChar) || char.IsWhiteSpace(e.KeyChar)) return;
        _search.Focus();
        _search.Text += e.KeyChar;
        _search.SelectionStart = _search.TextLength;
        e.Handled = true;
    }

    private void DrawEmptyState(Graphics g)
    {
        float s = S;
        const TextFormatFlags centered = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                                         TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        var title = new Rectangle((int)Math.Round(PadX * s), ViewportTopPx + (int)Math.Round(70 * s),
            ClientSize.Width - (int)Math.Round(2 * PadX * s), (int)Math.Round(28 * s));
        string heading = _query.Length > 0 ? $"No packs match “{_query}”" : "Nothing here yet";
        TextRenderer.DrawText(g, heading, _fonts.Title, title, Theme.Text, centered);
        var hint = new Rectangle(title.X, title.Bottom + (int)Math.Round(6 * s), title.Width, (int)Math.Round(22 * s));
        string tip = _filter != null && _query.Length > 0 ? "Try All, or search by pack name, style or author" : "Try a pack name, style or author";
        TextRenderer.DrawText(g, tip, _fonts.Label, hint, Theme.TextSecondary, centered);
    }
}
