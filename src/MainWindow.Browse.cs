using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Cursors;

internal sealed class NavItem
{
    public string View; // see MainWindow.View*; null for a group title
    public string Label;
    public int Count;
    public bool Header => View == null;
    public RectangleF Bounds; // client coordinates
    public Smooth Hover, Selected;
}

/// <summary>
/// Browsing the library: the sidebar of views and built-in styles, the header with the view's title, count, sort and
/// filters, and which cards a view shows in what order.
/// </summary>
internal sealed partial class MainWindow
{
    private const float SidebarWidth = 208, HeaderHeight = 56, NavItemHeight = 30, NavGroupHeight = 30;
    private const string ViewAll = "all", ViewBuiltIn = "builtin", ViewCommunity = "community", ViewMine = "mine", StylePrefix = "style:";

    private enum SortOrder
    {
        Popular,
        TopRated,
        Name,
    }

    private readonly List<NavItem> _nav = new();
    private readonly Dictionary<string, int> _textWidths = new();
    private readonly Dictionary<CursorPack, Card> _cardByPack = new();
    private List<Card> _communityOrder; // community cards in the current sort order, built on demand
    private string _view = ViewAll;
    private SortOrder _sort = SortOrder.Popular;
    private bool _onlyFull, _onlyAnimated, _onlyTopRated, _onlyOpen;
    private Smooth _sortHover, _filtersHover;
    private int _viewCount; // packs shown, not counting the Add pack card
    private float _gridLeft, _gridRight;

    private int SidebarPx => (int)Math.Round(SidebarWidth * S);
    private int ActiveFilterCount => (_onlyFull ? 1 : 0) + (_onlyAnimated ? 1 : 0) + (_onlyTopRated ? 1 : 0) + (_onlyOpen ? 1 : 0);
    private bool ViewHasCommunity => _nav.Any(n => n.View == ViewCommunity) && (_view == ViewAll || _view == ViewCommunity);

    private static bool IsBuiltIn(CursorPack pack) => pack.Kind == PackKind.Windows || pack.Kind == PackKind.Bundled;
    private static bool IsMine(CursorPack pack) => pack.Kind == PackKind.Library || pack.Kind == PackKind.User || pack.Kind == PackKind.Previous;
    private static bool IsCommunity(CursorPack pack) => pack?.Category == CommunityCatalog.Category;

    // ---- Settings --------------------------------------------------------------------------------

    private void LoadBrowseSettings()
    {
        string Get(string key) => _settings.TryGetValue(key, out var value) ? value : "";
        string view = Get("view");
        if (view.Length == 0)
        {
            // Settings from 1.1 remember a category chip instead.
            string category = Get("filter");
            view = category.Length == 0 ? ViewAll
                : category == CommunityCatalog.Category ? ViewCommunity
                : category == PackLibrary.AddedCategory ? ViewMine
                : StylePrefix + category;
        }
        _view = view;
        _sort = Get("sort") switch
        {
            "rated" => SortOrder.TopRated,
            "name" => SortOrder.Name,
            _ => SortOrder.Popular,
        };
        var filters = new HashSet<string>(Get("filters").Split(','), StringComparer.OrdinalIgnoreCase);
        _onlyFull = filters.Contains("full");
        _onlyAnimated = filters.Contains("animated");
        _onlyTopRated = filters.Contains("toprated");
        _onlyOpen = filters.Contains("open");
    }

    private void SaveBrowseSettings()
    {
        _settings.Remove("filter");
        _settings["view"] = _view;
        _settings["sort"] = _sort == SortOrder.TopRated ? "rated" : _sort == SortOrder.Name ? "name" : "popular";
        _settings["filters"] = string.Join(",", new[]
        {
            _onlyFull ? "full" : null, _onlyAnimated ? "animated" : null, _onlyTopRated ? "toprated" : null, _onlyOpen ? "open" : null,
        }.Where(f => f != null));
    }

    // ---- Views -----------------------------------------------------------------------------------

    /// <summary>Rebuilds the sidebar and its counts after the library loads.</summary>
    private void BuildNav()
    {
        var packs = _allCards.Where(c => !c.IsAdd).Select(c => c.Pack).ToList();
        _nav.Clear();
        _communityOrder = null;
        void Add(string view, string label, int count) => _nav.Add(new NavItem { View = view, Label = label, Count = count });

        _nav.Add(new NavItem { Label = "Library" });
        Add(ViewAll, "All packs", packs.Count);
        Add(ViewBuiltIn, "Built-in", packs.Count(IsBuiltIn));
        int community = packs.Count(IsCommunity);
        if (community > 0) Add(ViewCommunity, "Community", community);
        Add(ViewMine, "My packs", packs.Count(IsMine));

        _nav.Add(new NavItem { Label = "Styles" });
        foreach (var style in packs.Where(IsBuiltIn).GroupBy(p => p.Category))
            Add(StylePrefix + style.Key, style.Key, style.Count());

        if (_nav.All(n => n.View != _view)) _view = ViewAll;
        foreach (var item in _nav) item.Selected.Snap(item.View == _view ? 1 : 0);

        _catalogShown.Clear();
        foreach (var pack in packs)
            if (pack.Kind == PackKind.Catalog && pack.Preview != null) _catalogShown.Add(pack);
    }

    private void SetView(string view)
    {
        if (_view == view) return;
        _view = view;
        foreach (var item in _nav) item.Selected.Target = item.View == view ? 1 : 0;
        Invalidate(new Rectangle(SidebarPx, TitleBarPx, ClientSize.Width - SidebarPx, ViewportTopPx - TitleBarPx));
        ApplyFilter(animate: true);
    }

    /// <summary>The cards the current view, filters and search show, in display order.</summary>
    private List<Card> CardsInView()
    {
        bool Passes(Card c) => c.IsAdd ? _queryWords.Length == 0 : MatchesFilters(c.Pack) && MatchesSearch(c);

        if (_view == ViewCommunity) return CommunityOrder().Where(Passes).ToList();

        var result = new List<Card>();
        bool communityPlaced = false;
        foreach (var card in _allCards)
        {
            var pack = card.Pack;
            if (_view == ViewAll && IsCommunity(pack))
            {
                // The Community section follows the chosen sort; everything else keeps the library's order.
                if (!communityPlaced) result.AddRange(CommunityOrder().Where(Passes));
                communityPlaced = true;
                continue;
            }
            bool inView = _view switch
            {
                ViewAll => true,
                ViewBuiltIn => pack != null && IsBuiltIn(pack),
                ViewMine => card.IsAdd || IsMine(pack),
                _ => pack != null && IsBuiltIn(pack) && StylePrefix + pack.Category == _view,
            };
            if (inView && Passes(card)) result.Add(card);
        }
        return result;
    }

    private List<Card> CommunityOrder()
    {
        if (_communityOrder != null) return _communityOrder;
        var cards = _allCards.Where(c => IsCommunity(c.Pack));
        _communityOrder = (_sort switch
        {
            SortOrder.TopRated => cards.OrderByDescending(c => c.Pack.TopRated).ThenBy(c => c.Pack.Order),
            SortOrder.Name => cards.OrderBy(c => c.Pack.Label, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Pack.Order),
            _ => cards.OrderBy(c => c.Pack.Order),
        }).ToList();
        return _communityOrder;
    }

    /// <summary>The Filters menu narrows community sets only; built-in and added packs always show.</summary>
    private bool MatchesFilters(CursorPack pack)
    {
        if (ActiveFilterCount == 0 || !IsCommunity(pack) || !ViewHasCommunity) return true;
        return (!_onlyFull || pack.CatalogRoles >= 15)
               && (!_onlyAnimated || pack.CatalogAnimated > 0)
               && (!_onlyTopRated || pack.TopRated)
               && (!_onlyOpen || CommunityCatalog.IsOpenLicense(pack.License));
    }

    // ---- Visible range ---------------------------------------------------------------------------

    /// <summary>Indices of the cards inside the viewport, widened by margin pixels above and below.</summary>
    private void VisibleRange(float margin, out int first, out int last)
    {
        // Cards are laid out top to bottom, so both ends are found by binary search.
        float top = _scroll.Value - margin, bottom = _scroll.Value + ViewportHeight + margin;
        int lo = 0, hi = _cards.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_cards[mid].Bounds.Bottom < top) lo = mid + 1;
            else hi = mid;
        }
        first = lo;
        hi = _cards.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_cards[mid].Bounds.Top <= bottom) lo = mid + 1;
            else hi = mid;
        }
        last = lo - 1;
    }

    private bool IsNearView(Card card) =>
        card.Visible && card.Bounds.Bottom >= _scroll.Value - ViewportHeight && card.Bounds.Top <= _scroll.Value + 2 * ViewportHeight;

    // ---- Sidebar ---------------------------------------------------------------------------------

    private void LayoutNav()
    {
        float s = S, x = 12 * s, w = SidebarPx - 24 * s, y = TitleBarPx + 4 * s;
        for (int i = 0; i < _nav.Count; i++)
        {
            var item = _nav[i];
            if (item.Header && i > 0) y += 12 * s;
            float h = (item.Header ? NavGroupHeight : NavItemHeight) * s;
            item.Bounds = new RectangleF(x, (float)Math.Round(y), w, (float)Math.Round(h));
            y += h + (item.Header ? 0 : 2 * s);
        }
    }

    private void DrawSidebar(Graphics g)
    {
        float s = S;
        int top = TitleBarPx, width = SidebarPx, line = Math.Max(1, (int)Math.Round(s));
        using (var bg = new SolidBrush(Theme.Sidebar)) g.FillRectangle(bg, 0, top, width, ClientSize.Height - top);
        using (var divider = new SolidBrush(Theme.Divider)) g.FillRectangle(divider, width - line, top, line, ClientSize.Height - top);

        const TextFormatFlags oneLine = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding |
                                        TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var item in _nav)
        {
            var r = item.Bounds;
            if (item.Header)
            {
                var headerRect = Rectangle.Round(new RectangleF(r.X + 12 * s, r.Y, r.Width - 24 * s, r.Height));
                TextRenderer.DrawText(g, item.Label, _fonts.Small, headerRect, Theme.SectionCount, Theme.Sidebar, oneLine | TextFormatFlags.Left);
                continue;
            }

            float hover = item.Hover.Value, selected = item.Selected.Value;
            var fill = Theme.Lerp(Theme.Lerp(Color.FromArgb(0, 255, 255, 255), Theme.NavHover, hover), Theme.NavSelected, selected);
            if (fill.A > 0)
                using (var path = Theme.RoundedRect(r, 6 * s))
                using (var brush = new SolidBrush(fill))
                    g.FillPath(brush, path);
            if (selected > 0.01f)
            {
                float barH = 16 * s * Theme.EaseOut(selected);
                using var path = Theme.RoundedRect(new RectangleF(r.X, r.Y + (r.Height - barH) / 2, 3 * s, barH), 1.5f * s);
                using var brush = new SolidBrush(Theme.Fade(Theme.NavAccent, selected));
                g.FillPath(brush, path);
            }

            var behind = OpaqueOver(Theme.Sidebar, fill);
            string count = item.Count.ToString("N0", CultureInfo.CurrentCulture);
            int countW = TextWidth(count, _fonts.Small);
            int right = (int)Math.Round(r.Right - 10 * s);
            var labelRect = new Rectangle((int)Math.Round(r.X + 14 * s), (int)r.Y, right - countW - (int)Math.Round(r.X + 22 * s), (int)r.Height);
            var textColor = Theme.Lerp(Theme.Lerp(Theme.ChipText, Theme.Text, hover), Theme.TextSelected, selected);
            TextRenderer.DrawText(g, item.Label, _fonts.Label, labelRect, textColor, behind, oneLine | TextFormatFlags.Left);
            TextRenderer.DrawText(g, count, _fonts.Small, new Rectangle(right - countW, (int)r.Y, countW, (int)r.Height),
                Theme.Lerp(Theme.SectionCount, Theme.TextSecondary, selected), behind, oneLine | TextFormatFlags.Right);
        }
    }

    // ---- Header ----------------------------------------------------------------------------------

    private string SortLabel => _sort switch
    {
        SortOrder.TopRated => "Top rated",
        SortOrder.Name => "Name",
        _ => "Most downloaded",
    };

    private string FiltersLabel => ActiveFilterCount > 0 ? "Filters · " + ActiveFilterCount : "Filters";

    private Rectangle FiltersButtonRect() => HeaderButtonRect(filters: true);

    private Rectangle SortButtonRect() => HeaderButtonRect(filters: false);

    /// <summary>Sort and Filters sit at the right of the header when the view includes community sets.</summary>
    private Rectangle HeaderButtonRect(bool filters)
    {
        if (!ViewHasCommunity) return Rectangle.Empty;
        float s = S;
        int h = (int)Math.Round(ButtonHeight * s);
        int y = TitleBarPx + ((int)Math.Round(HeaderHeight * s) - h) / 2;
        int right = (int)Math.Round(_gridRight);
        int filtersW = HeaderButtonWidth(FiltersLabel);
        if (filters) return new Rectangle(right - filtersW, y, filtersW, h);
        int sortW = HeaderButtonWidth(SortLabel);
        return new Rectangle(right - filtersW - (int)Math.Round(8 * s) - sortW, y, sortW, h);
    }

    private int HeaderButtonWidth(string label) => TextWidth(label, _fonts.Button) + (int)Math.Round(60 * S);

    private string CountText()
    {
        string noun = _view == ViewCommunity ? (_viewCount == 1 ? "set" : "sets") : (_viewCount == 1 ? "pack" : "packs");
        int total = _nav.FirstOrDefault(n => n.View == _view)?.Count ?? _viewCount;
        string shown = _viewCount.ToString("N0", CultureInfo.CurrentCulture);
        return _viewCount == total ? shown + " " + noun : shown + " of " + total.ToString("N0", CultureInfo.CurrentCulture) + " " + noun;
    }

    private void DrawHeader(Graphics g)
    {
        float s = S;
        int left = SidebarPx, top = TitleBarPx, bottom = ViewportTopPx;
        using (var bg = new SolidBrush(Theme.Background)) g.FillRectangle(bg, left, top, ClientSize.Width - left, bottom - top);

        const TextFormatFlags oneLine = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        bool buttons = ViewHasCommunity;
        int x = (int)Math.Round(_gridLeft + 2 * s);
        int limit = buttons ? SortButtonRect().X - (int)Math.Round(16 * s) : (int)Math.Round(_gridRight);
        string title = _nav.FirstOrDefault(n => n.View == _view)?.Label ?? "All packs";
        int titleW = Math.Max(0, Math.Min(TextWidth(title, _fonts.Title) + 2, limit - x));
        TextRenderer.DrawText(g, title, _fonts.Title, new Rectangle(x, top, titleW, bottom - top), Theme.Text, Theme.Background, oneLine);
        int countX = x + titleW + (int)Math.Round(10 * s);
        if (countX < limit)
            TextRenderer.DrawText(g, CountText(), _fonts.Label, new Rectangle(countX, top + (int)Math.Round(2 * s), limit - countX, bottom - top),
                Theme.TextSecondary, Theme.Background, oneLine);

        if (buttons)
        {
            DrawHeaderButton(g, SortButtonRect(), "", SortLabel, _sortHover.Value, active: false);
            DrawHeaderButton(g, FiltersButtonRect(), "", FiltersLabel, _filtersHover.Value, active: ActiveFilterCount > 0);
        }

        if (_divider.Value > 0)
        {
            int line = Math.Max(1, (int)Math.Round(s));
            using var brush = new SolidBrush(Theme.Fade(Theme.Divider, _divider.Value));
            g.FillRectangle(brush, left, bottom - line, ClientSize.Width - left, line);
        }
    }

    private void DrawHeaderButton(Graphics g, Rectangle rb, string icon, string label, float hover, bool active)
    {
        float s = S;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var fill = Theme.Lerp(active ? Theme.NavSelected : Theme.ButtonFill, Theme.ButtonFillHover, hover);
        using (var path = Theme.RoundedRect(rb, 6 * s))
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);
        using (var path = Theme.RoundedRect(new RectangleF(rb.X + 0.5f, rb.Y + 0.5f, rb.Width - 1, rb.Height - 1), 6 * s))
        using (var pen = new Pen(active ? Theme.BorderHover : Theme.ButtonBorder, 1f))
            g.DrawPath(pen, path);

        const TextFormatFlags centered = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        var behind = OpaqueOver(Theme.Background, fill);
        int iconW = (int)Math.Round(32 * s), chevronW = (int)Math.Round(24 * s);
        TextRenderer.DrawText(g, icon, _fonts.Glyphs, new Rectangle(rb.X + (int)Math.Round(2 * s), rb.Y, iconW, rb.Height),
            active ? Theme.Text : Theme.TextSecondary, behind, centered);
        TextRenderer.DrawText(g, label, _fonts.Button, new Rectangle(rb.X + iconW, rb.Y, rb.Width - iconW - chevronW, rb.Height),
            active ? Theme.TextSelected : Theme.Text, behind, centered & ~TextFormatFlags.HorizontalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "", _fonts.Glyphs, new Rectangle(rb.Right - chevronW - (int)Math.Round(4 * s), rb.Y, chevronW, rb.Height),
            Theme.TextSecondary, behind, centered);
    }

    private void ShowSortMenu()
    {
        var r = SortButtonRect();
        int command = ShowMenu(new Point(r.X, r.Bottom + (int)Math.Round(4 * S)),
            (1, "Most downloaded", _sort == SortOrder.Popular, true),
            (2, "Top rated first", _sort == SortOrder.TopRated, true),
            (3, "Name (A to Z)", _sort == SortOrder.Name, true));
        var sort = command switch
        {
            1 => SortOrder.Popular,
            2 => SortOrder.TopRated,
            3 => SortOrder.Name,
            _ => _sort,
        };
        if (sort == _sort) return;
        _sort = sort;
        _communityOrder = null;
        ApplyFilter(animate: true);
    }

    private void ShowFiltersMenu()
    {
        var r = FiltersButtonRect();
        int command = ShowMenu(new Point(r.X, r.Bottom + (int)Math.Round(4 * S)),
            (1, "Full sets, all 15 cursors", _onlyFull, true),
            (2, "Animated", _onlyAnimated, true),
            (3, "Top rated", _onlyTopRated, true),
            (4, "Open licenses (public domain, CC BY)", _onlyOpen, true),
            (0, null, false, true),
            (5, "Clear filters", false, ActiveFilterCount > 0));
        switch (command)
        {
            case 1: _onlyFull = !_onlyFull; break;
            case 2: _onlyAnimated = !_onlyAnimated; break;
            case 3: _onlyTopRated = !_onlyTopRated; break;
            case 4: _onlyOpen = !_onlyOpen; break;
            case 5: _onlyFull = _onlyAnimated = _onlyTopRated = _onlyOpen = false; break;
            default: return;
        }
        ApplyFilter(animate: true);
    }

    private int ShowMenu(Point client, params (int Id, string Text, bool Checked, bool Enabled)[] items)
    {
        IntPtr menu = Native.CreatePopupMenu();
        try
        {
            foreach (var item in items)
            {
                if (item.Text == null)
                {
                    Native.AppendMenu(menu, Native.MF_SEPARATOR, UIntPtr.Zero, null);
                    continue;
                }
                uint flags = Native.MF_STRING | (item.Checked ? Native.MF_CHECKED : 0) | (item.Enabled ? 0 : Native.MF_GRAYED);
                Native.AppendMenu(menu, flags, (UIntPtr)item.Id, item.Text);
            }
            var screen = PointToScreen(client);
            Native.SetForegroundWindow(Handle);
            return Native.TrackPopupMenuEx(menu, Native.TPM_RETURNCMD | Native.TPM_RIGHTBUTTON, screen.X, screen.Y, Handle, IntPtr.Zero);
        }
        finally
        {
            Native.DestroyMenu(menu);
        }
    }

    // ---- Animation and helpers -------------------------------------------------------------------

    private bool StepNav(float dt)
    {
        bool busy = false;
        foreach (var item in _nav)
        {
            if (!(item.Hover.Step(dt, 45) | item.Selected.Step(dt, 60))) continue;
            busy = true;
            var r = Rectangle.Round(item.Bounds);
            r.Inflate(2, 2);
            Invalidate(r);
        }
        if (_sortHover.Step(dt, 45) | _filtersHover.Step(dt, 45))
        {
            busy = true;
            Invalidate(new Rectangle(SidebarPx, TitleBarPx, ClientSize.Width - SidebarPx, ViewportTopPx - TitleBarPx));
        }
        return busy;
    }

    private int TextWidth(string text, Font font)
    {
        string key = font.Size.ToString(CultureInfo.InvariantCulture) + "|" + text;
        if (!_textWidths.TryGetValue(key, out int width))
            _textWidths[key] = width = TextRenderer.MeasureText(text, font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
        return width;
    }

    /// <summary>The solid color of a translucent fill over a background, for GDI text that ignores alpha.</summary>
    private static Color OpaqueOver(Color background, Color fill) =>
        Theme.Lerp(background, Color.FromArgb(255, fill.R, fill.G, fill.B), fill.A / 255f);
}
