using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Cursors;

internal sealed class Card
{
    public CursorPack Pack; // null for the "Add pack" card
    public string Category;
    public RectangleF Bounds; // content coordinates (unscrolled)
    public Smooth Hover, Press, Selected, Appear, Peek;
    public long HoverStart;

    public bool IsAdd => Pack == null;
}

internal sealed class Chip
{
    public string Category; // null = everything
    public string Label;
    public RectangleF Bounds; // client coordinates; the chip bar does not scroll
    public Smooth Hover, Selected;
}

internal sealed class Section
{
    public string Title;
    public int Count;
    public RectangleF Bounds; // content coordinates
}

internal sealed partial class MainWindow : Form
{
    // Layout metrics in 96-DPI pixels.
    private const float TitleBarHeight = 48, CaptionButtonWidth = 46, PadX = 24, PadBottom = 28, Gap = 12, MinCardSize = 152,
        CardRadius = 10, PreviewBox = 72, PeekBox = 20, ButtonHeight = 32, ChipHeight = 28, ChipGap = 6, ChipRowGap = 8,
        ChipBarPadBottom = 6, SectionHeaderHeight = 40, SectionGap = 14;

    // The roles revealed under the pointer when a card is hovered.
    private static readonly int[] PeekRoles = { CursorPack.Hand, CursorPack.IBeam, CursorPack.Wait };

    private int _dpi;
    private UiFonts _fonts;
    private readonly Dictionary<string, string> _settings;

    private PackLibrary _library;
    private readonly List<Card> _allCards = new();
    private readonly List<Card> _cards = new(); // visible under the current filter
    private readonly List<Chip> _chips = new();
    private readonly List<Section> _sections = new();
    private readonly Dictionary<string, int> _chipTextWidths = new();
    private string _filter; // category, or null for all
    private string _selectedId;
    private readonly CursorApplier _applier = new();

    private readonly Dictionary<string, CursorPreview> _previewCache = new();
    private readonly HashSet<CursorPreview> _stalePreviews = new();
    private readonly HashSet<CursorPack> _peekLoading = new();
    private int _previewGeneration;

    private int _columns = 1;
    private float _contentHeight, _chipBarHeight;
    private Smooth _scroll, _divider;

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastTick;
    private Rectangle _normalBounds; // last non-maximized, non-minimized bounds, saved on exit

    private float S => _dpi / 96f;
    private int TitleBarPx => (int)Math.Round(TitleBarHeight * S);
    private int ViewportTopPx => TitleBarPx + (int)Math.Round(_chipBarHeight);
    private float ViewportTop => ViewportTopPx;
    private float ViewportHeight => Math.Max(0, ClientSize.Height - ViewportTop);
    private float MaxScroll => Math.Max(0, _contentHeight - ViewportHeight);
    private bool HasScroll => MaxScroll > 0.5f;

    public MainWindow()
    {
        Text = "Cursors";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        AllowDrop = true;
        KeyPreview = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch { }

        _dpi = DeviceDpi > 0 ? DeviceDpi : 96;
        _fonts = new UiFonts(S);
        InitSearch();
        _settings = IniFile.Read(AppPaths.SettingsFile);
        _filter = _settings.TryGetValue("filter", out var filter) && filter.Length > 0 ? filter : null;
        _timer.Tick += (_, _) => OnTick();
        _applier.Completed += OnApplyCompleted;

        PackLibrary.SnapshotCurrentOnce();
        ReloadLibrary();
        RestoreWindowBounds();
    }

    // ---- Lifecycle -----------------------------------------------------------------------------

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int dpi = (int)Native.GetDpiForWindow(Handle);
            if (dpi > 0 && dpi != _dpi) UpdateDpi(dpi);
        }
        catch (EntryPointNotFoundException) { }

        MinimumSize = new Size((int)(520 * S), (int)(420 * S));
        Native.ApplyDarkFrame(Handle, Native.ToColorRef(Theme.FrameBorder));
        Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
            Native.SWP_FRAMECHANGED | Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        StartPreviewLoad();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _applier.WaitIdle(3000);
        SaveSettings();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _fonts.Dispose();
            foreach (var preview in _previewCache.Values.Concat(_stalePreviews).Where(p => p != null).Distinct()) preview.Dispose();
            foreach (var card in _allCards) DropRolePreviews(card.Pack);
        }
        base.Dispose(disposing);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        TrackNormalBounds();
        LayoutCards();
        Invalidate();
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        TrackNormalBounds();
    }

    private void TrackNormalBounds()
    {
        if (IsHandleCreated && !Native.IsZoomed(Handle) && !Native.IsIconic(Handle)) _normalBounds = Bounds;
    }

    private void RestoreWindowBounds()
    {
        var area = Screen.PrimaryScreen.WorkingArea;
        int w = Math.Min(area.Width, (int)(980 * S)), h = Math.Min(area.Height, (int)(720 * S));
        var bounds = new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);

        if (_settings.TryGetValue("bounds", out var saved))
        {
            var parts = saved.Split(',');
            if (parts.Length == 4 && parts.All(p => int.TryParse(p, out _)))
            {
                var r = new Rectangle(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
                bool visible = Screen.AllScreens.Any(sc =>
                {
                    var overlap = Rectangle.Intersect(sc.WorkingArea, r);
                    return overlap.Width >= 120 && overlap.Height >= 80;
                });
                if (visible && r.Width >= 300 && r.Height >= 240) bounds = r;
            }
        }
        Bounds = _normalBounds = bounds;
        if (_settings.TryGetValue("maximized", out var max) && max == "1") WindowState = FormWindowState.Maximized;
    }

    private void SaveSettings()
    {
        var r = WindowState == FormWindowState.Normal ? Bounds : _normalBounds;
        _settings["bounds"] = $"{r.X},{r.Y},{r.Width},{r.Height}";
        _settings["maximized"] = WindowState == FormWindowState.Maximized ? "1" : "0";
        _settings["selected"] = _selectedId ?? "";
        _settings["filter"] = _filter ?? "";
        IniFile.Write(AppPaths.SettingsFile, _settings);
    }

    private void UpdateDpi(int dpi)
    {
        _dpi = dpi;
        var oldFonts = _fonts;
        _fonts = new UiFonts(S);
        if (_search != null) _search.Font = _fonts.Label;
        oldFonts.Dispose();
        _restoreTextWidth = -1;
        _titleTextWidth = -1;
        _chipTextWidths.Clear();
        if (IsHandleCreated) MinimumSize = new Size((int)(520 * S), (int)(420 * S));

        // Keep showing the old previews until sharper ones for the new DPI are ready.
        foreach (var preview in _previewCache.Values)
            if (preview != null) _stalePreviews.Add(preview);
        _previewCache.Clear();
        foreach (var card in _allCards) DropRolePreviews(card.Pack);
        LayoutCards();
        if (IsHandleCreated) StartPreviewLoad(reloadAll: true);
        Invalidate();
    }

    // ---- Library, filters and layout -----------------------------------------------------------

    private void ReloadLibrary(ICollection<string> newIds = null)
    {
        var previous = _allCards.Where(c => c.Pack != null).ToDictionary(c => c.Pack.Id, c => c.Pack);
        _library = PackLibrary.Load();
        _selectedId = CursorScheme.FindActive(_library.Packs, _library.WindowsDefault,
            _selectedId ?? (_settings.TryGetValue("selected", out var s) ? s : null))?.Id;

        var carried = new HashSet<string>();
        _allCards.Clear();
        foreach (var pack in _library.Packs)
        {
            var card = new Card { Pack = pack, Category = pack.Category };
            if (previous.TryGetValue(pack.Id, out var old) && newIds?.Contains(pack.Id) != true)
            {
                pack.Preview = old.Preview;
                pack.RolePreviews = old.RolePreviews;
                carried.Add(pack.Id);
                if (pack.Preview != null) card.Appear.Snap(1);
            }
            card.Selected.Snap(pack.Id == _selectedId ? 1 : 0);
            _allCards.Add(card);
        }
        foreach (var old in previous.Values)
            if (!carried.Contains(old.Id)) DropRolePreviews(old);

        var add = new Card { Category = PackLibrary.AddedCategory };
        add.Appear.Snap(1);
        _allCards.Add(add);

        _chips.Clear();
        _chips.Add(new Chip { Label = "All" });
        // Only categories that hold packs get a chip; "Added" appears once the user adds something.
        foreach (string category in _allCards.Where(c => !c.IsAdd).Select(c => c.Category).Distinct())
            _chips.Add(new Chip { Category = category, Label = category });
        if (_filter != null && _chips.All(c => c.Category != _filter)) _filter = null;
        foreach (var chip in _chips) chip.Selected.Snap(chip.Category == _filter ? 1 : 0);

        ApplyFilter(animate: false);
        if (IsHandleCreated) StartPreviewLoad();
    }

    private void SetFilter(string category)
    {
        if (_filter == category) return;
        _filter = category;
        foreach (var chip in _chips) chip.Selected.Target = chip.Category == category ? 1 : 0;
        ApplyFilter(animate: true);
    }

    private void ApplyFilter(bool animate)
    {
        foreach (var card in _cards)
        {
            card.Hover.Snap(0);
            card.Press.Snap(0);
            card.Peek.Snap(0);
        }
        _cards.Clear();
        _cards.AddRange(_allCards.Where(c => (_filter == null || c.Category == _filter) && MatchesSearch(c)));
        if (animate)
        {
            foreach (var card in _cards)
            {
                if (!card.IsAdd && card.Pack.Preview == null) continue;
                card.Appear.Value = 0.3f;
                card.Appear.Target = 1;
            }
        }
        _hoverIndex = _pressIndex = -1;
        _focusIndex = Math.Min(_focusIndex, _cards.Count - 1);
        LayoutCards();
        if (animate)
        {
            _scroll.Snap(0);
            if (IsHandleCreated && ClientRectangle.Contains(PointToClient(MousePosition))) UpdateHover(PointToClient(MousePosition));
            StartAnimation();
        }
        Invalidate();
    }

    private void LayoutCards()
    {
        LayoutChips();
        LayoutSearch();
        float s = S, gap = Gap * s, avail = ClientSize.Width - 2 * PadX * s;
        _columns = Math.Max(1, (int)((avail + gap) / (MinCardSize * s + gap)));
        float size = Math.Max(60 * s, (float)Math.Floor((avail - gap * (_columns - 1)) / _columns));
        float used = size * _columns + gap * (_columns - 1);
        float left = (float)Math.Round((ClientSize.Width - used) / 2);

        _sections.Clear();
        float y = _filter == null ? 0 : 10 * s;
        for (int start = 0; start < _cards.Count;)
        {
            int end = _cards.Count;
            if (_filter == null)
            {
                end = start + 1;
                while (end < _cards.Count && _cards[end].Category == _cards[start].Category) end++;
                if (_sections.Count > 0) y += SectionGap * s;
                _sections.Add(new Section
                {
                    Title = _cards[start].Category,
                    Count = _cards.Skip(start).Take(end - start).Count(c => !c.IsAdd),
                    Bounds = new RectangleF(left, y, used, SectionHeaderHeight * s),
                });
                y += SectionHeaderHeight * s;
            }
            int n = end - start;
            for (int k = 0; k < n; k++)
                _cards[start + k].Bounds = new RectangleF((float)Math.Round(left + (k % _columns) * (size + gap)),
                    (float)Math.Round(y + (k / _columns) * (size + gap)), size, size);
            int rows = (n + _columns - 1) / _columns;
            y += rows * size + Math.Max(0, rows - 1) * gap;
            start = end;
        }
        if (_filter != null) y += 4 * s;
        _contentHeight = y + PadBottom * s;
        _scroll.Target = Clamp(_scroll.Target, 0, MaxScroll);
        _scroll.Value = Clamp(_scroll.Value, 0, MaxScroll);
    }

    private void LayoutChips()
    {
        float s = S, h = ChipHeight * s, x0 = PadX * s, x = x0, y = TitleBarPx, maxX = ClientSize.Width - PadX * s;
        int rows = 1;
        foreach (var chip in _chips)
        {
            if (!_chipTextWidths.TryGetValue(chip.Label, out int textWidth))
                _chipTextWidths[chip.Label] = textWidth = TextRenderer.MeasureText(chip.Label, _fonts.Chip, Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            float w = textWidth + 24 * s;
            if (x + w > maxX && x > x0 + 1)
            {
                x = x0;
                y += h + ChipRowGap * s;
                rows++;
            }
            chip.Bounds = new RectangleF((float)Math.Round(x), (float)Math.Round(y), (float)Math.Round(w), (float)Math.Round(h));
            x += w + ChipGap * s;
        }
        _chipBarHeight = rows * h + (rows - 1) * ChipRowGap * s + ChipBarPadBottom * s;
    }

    private RectangleF ScreenRect(RectangleF content) =>
        new(content.X, content.Y + ViewportTop - _scroll.Value, content.Width, content.Height);

    private RectangleF ScreenRect(Card card) => ScreenRect(card.Bounds);

    // ---- Previews ------------------------------------------------------------------------------

    private void StartPreviewLoad(bool reloadAll = false)
    {
        int generation = ++_previewGeneration;
        ResetCatalogQueue();
        int box = (int)Math.Round(PreviewBox * S);
        var packs = _allCards.Where(c => c.Pack != null && (reloadAll || c.Pack.Preview == null)).Select(c => c.Pack).ToList();

        ThreadPool.QueueUserWorkItem(_ =>
        {
            var decoded = new Dictionary<string, CursorPreview>(StringComparer.OrdinalIgnoreCase);
            foreach (var pack in packs)
            {
                // Community cards show the site's image until they're downloaded; uncached images load as cards come into view.
                bool image = pack.Kind == PackKind.Catalog;
                string path = image ? CommunityCatalog.CachedImage(pack.CatalogPreviews?[0]) : pack.PreviewPath;
                if (path == null) continue;
                if (!decoded.TryGetValue(path, out var preview))
                {
                    preview = image ? CursorDecoder.CreateImagePreview(path, box) : CursorDecoder.CreatePreview(path, box);
                    decoded[path] = preview;
                }
                var result = preview;
                try
                {
                    BeginInvoke((Action)(() => OnPreviewReady(generation, box, path, pack, result)));
                }
                catch (InvalidOperationException)
                {
                    return; // window closed
                }
            }
            try
            {
                BeginInvoke((Action)(() => OnPreviewBatchDone(generation)));
            }
            catch (InvalidOperationException) { }
        });
    }

    private void OnPreviewReady(int generation, int box, string path, CursorPack pack, CursorPreview preview)
    {
        string key = box + "|" + path;
        if (generation != _previewGeneration || box != (int)Math.Round(PreviewBox * S))
        {
            if (preview != null && !_previewCache.ContainsValue(preview)) _stalePreviews.Add(preview);
            return;
        }
        if (_previewCache.TryGetValue(key, out var cached))
        {
            if (preview != null && preview != cached) _stalePreviews.Add(preview);
            preview = cached;
        }
        else _previewCache[key] = preview;
        if (preview == null) return;

        var card = _allCards.FirstOrDefault(c => c.Pack == pack);
        if (card == null || pack.Preview == preview) return;
        if (pack.Preview == null && _cards.Contains(card))
        {
            card.Appear.Value = 0;
            card.Appear.Target = 1;
            StartAnimation();
        }
        else if (pack.Preview == null)
        {
            card.Appear.Snap(1);
        }
        pack.Preview = preview;
        InvalidateCard(card);
    }

    private void OnPreviewBatchDone(int generation)
    {
        if (generation != _previewGeneration) return;
        _previewBatchPending = false;
        InvalidateGrid(); // cards on screen then ask for Community previews that weren't cached
        var inUse = new HashSet<CursorPreview>(_allCards.Where(c => c.Pack?.Preview != null).Select(c => c.Pack.Preview));
        foreach (var preview in _stalePreviews.ToList())
        {
            if (inUse.Contains(preview) || _previewCache.ContainsValue(preview)) continue;
            preview.Dispose();
            _stalePreviews.Remove(preview);
        }
    }

    /// <summary>Decodes the small link/text/busy previews the first time a card is hovered.</summary>
    private void EnsureRolePreviews(Card card)
    {
        var pack = card.Pack;
        if (pack == null || pack.RolePreviews != null || !_peekLoading.Add(pack)) return;
        int box = (int)Math.Round(PeekBox * S), generation = _previewGeneration;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var previews = pack.Kind == PackKind.Catalog
                ? (pack.CatalogPreviews ?? new string[4]).Skip(1).Select(id => CommunityCatalog.FetchImage(id) is string image ? CursorDecoder.CreateImagePreview(image, box) : null).ToArray()
                : PeekRoles.Select(role => pack.SourcePathFor(role) is string path ? CursorDecoder.CreatePreview(path, box) : null).ToArray();
            try
            {
                BeginInvoke((Action)(() =>
                {
                    _peekLoading.Remove(pack);
                    if (generation != _previewGeneration || pack.RolePreviews != null)
                    {
                        foreach (var p in previews) p?.Dispose();
                        return;
                    }
                    pack.RolePreviews = previews;
                    InvalidateCard(card);
                    StartAnimation();
                }));
            }
            catch (InvalidOperationException)
            {
                foreach (var p in previews) p?.Dispose();
            }
        });
    }

    private static void DropRolePreviews(CursorPack pack)
    {
        if (pack?.RolePreviews == null) return;
        foreach (var preview in pack.RolePreviews) preview?.Dispose();
        pack.RolePreviews = null;
    }

    // ---- Actions -------------------------------------------------------------------------------

    private void Activate(Card card)
    {
        if (card.IsAdd)
        {
            ShowAddMenu(card);
            return;
        }
        if (card.Pack.Kind == PackKind.Catalog)
        {
            DownloadCatalogPack(card);
            return;
        }
        SetSelected(card.Pack.Id);
        _applier.Request(card.Pack, _library.WindowsDefault);
    }

    private void RestoreDefault()
    {
        var pack = _library.WindowsDefault;
        SetSelected(pack.Id);
        _applier.Request(pack, pack);
        ShowToast("Windows default cursors restored");
    }

    private void SetSelected(string id)
    {
        _selectedId = id;
        foreach (var card in _allCards)
        {
            float target = card.Pack != null && card.Pack.Id == id ? 1 : 0;
            card.Selected.Target = target;
            if (!_cards.Contains(card)) card.Selected.Snap(target);
        }
        StartAnimation();
    }

    private void OnApplyCompleted(CursorPack pack, Exception error)
    {
        if (error == null || IsDisposed) return;
        try
        {
            BeginInvoke((Action)(() => ShowToast($"Couldn't apply “{pack.Label}”")));
        }
        catch (InvalidOperationException) { }
    }

    /// <summary>Keeps the active state honest when cursors change elsewhere (e.g. Windows Settings).</summary>
    private void RefreshActiveFromSystem()
    {
        if (!_applier.WaitIdle(0) || _library == null) return;
        var active = CursorScheme.FindActive(_library.Packs, _library.WindowsDefault, _selectedId);
        if (active?.Id != _selectedId) SetSelected(active?.Id);
    }

    private void BrowseForPacks()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Add cursor packs",
            Filter = "Cursor packs (*.zip, *.inf, *.cur, *.ani)|*.zip;*.inf;*.cur;*.ani|All files (*.*)|*.*",
            Multiselect = true,
            RestoreDirectory = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) ImportAsync(dialog.FileNames);
    }

    private void ImportAsync(string[] paths)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            ImportResult result;
            try
            {
                result = PackLibrary.Import(paths);
            }
            catch
            {
                result = new ImportResult();
            }
            try
            {
                BeginInvoke((Action)(() => OnImported(result)));
            }
            catch (InvalidOperationException) { }
        });
    }

    private void OnImported(ImportResult result)
    {
        if (result.AddedIds.Count == 0 && result.ExistingIds.Count == 0)
        {
            ShowToast("No cursor files found");
            return;
        }
        ReloadLibrary(result.AddedIds);
        string focusId = result.AddedIds.FirstOrDefault() ?? result.ExistingIds.First();
        if (_cards.All(c => c.Pack?.Id != focusId)) SetFilter(null);
        int index = _cards.FindIndex(c => c.Pack?.Id == focusId);
        if (index >= 0) ScrollIntoView(index);

        if (result.AddedIds.Count == 0) ShowToast("Already in your library");
        else if (result.AddedIds.Count == 1 && index >= 0) ShowToast($"Added “{_cards[index].Pack.Label}”");
        else ShowToast($"Added {result.AddedIds.Count} cursor packs");
    }

    private void ShowContextMenu(Card card, Point screen)
    {
        var pack = card.Pack;
        if (pack == null) return;
        string folder = pack.Folder ?? (pack.PreviewPath is string p ? Path.GetDirectoryName(p) : null);
        bool hasFolder = folder != null && Directory.Exists(folder);
        bool hasUrl = Uri.TryCreate(pack.Url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
        string credit = string.Join(" · ", new[] { pack.Author, pack.License, pack.Downloads > 0 ? CommunityCatalog.FormatDownloads(pack.Downloads) : null }.Where(x => !string.IsNullOrWhiteSpace(x)));

        IntPtr menu = Native.CreatePopupMenu();
        try
        {
            bool remote = pack.Kind == PackKind.Catalog;
            Native.AppendMenu(menu, Native.MF_STRING, (UIntPtr)1, remote ? "Download and apply" : "Apply");
            Native.AppendMenu(menu, Native.MF_SEPARATOR, UIntPtr.Zero, null);
            if (hasUrl) Native.AppendMenu(menu, Native.MF_STRING, (UIntPtr)4, pack.Category == CommunityCatalog.Category ? "Visit set page" : "Visit project page");
            if (!remote) Native.AppendMenu(menu, Native.MF_STRING | (hasFolder ? 0 : Native.MF_GRAYED), (UIntPtr)2, "Open folder");
            if (pack.Kind == PackKind.Library)
            {
                Native.AppendMenu(menu, Native.MF_SEPARATOR, UIntPtr.Zero, null);
                Native.AppendMenu(menu, Native.MF_STRING, (UIntPtr)3, "Remove from library");
            }
            if (credit.Length > 0)
            {
                Native.AppendMenu(menu, Native.MF_SEPARATOR, UIntPtr.Zero, null);
                Native.AppendMenu(menu, Native.MF_STRING | Native.MF_GRAYED, UIntPtr.Zero, credit.Replace("&", "&&"));
            }
            // A popup menu only dismisses reliably (click outside, Escape) when its owner is the foreground window.
            Native.SetForegroundWindow(Handle);
            int command = Native.TrackPopupMenuEx(menu, Native.TPM_RETURNCMD | Native.TPM_RIGHTBUTTON, screen.X, screen.Y, Handle, IntPtr.Zero);
            switch (command)
            {
                case 1:
                    Activate(card);
                    break;
                case 2:
                    Process.Start("explorer.exe", "\"" + folder + "\"");
                    break;
                case 3:
                    RemovePack(pack);
                    break;
                case 4:
                    Process.Start(uri.AbsoluteUri);
                    break;
            }
        }
        finally
        {
            Native.DestroyMenu(menu);
        }
    }

    private void RemovePack(CursorPack pack)
    {
        if (pack.Id == _selectedId)
        {
            // Switch away first so Windows isn't left pointing at files in the Recycle Bin.
            SetSelected(_library.WindowsDefault.Id);
            _applier.Request(_library.WindowsDefault, _library.WindowsDefault);
            _applier.WaitIdle(3000);
        }
        try
        {
            PackLibrary.Remove(pack);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            ShowToast($"Couldn't remove “{pack.Label}”");
            return;
        }
        ReloadLibrary();
        ShowToast($"Removed “{pack.Label}”");
    }

    private void ScrollIntoView(int index)
    {
        if (index < 0 || index >= _cards.Count) return;
        var b = _cards[index].Bounds;
        float s = S, target = _scroll.Target;
        float top = b.Y - (_filter == null ? SectionHeaderHeight * s : 0);
        if (top < target) target = top;
        else if (b.Bottom + PadBottom * s > target + ViewportHeight) target = b.Bottom + PadBottom * s - ViewportHeight;
        _scroll.Target = Clamp(target, 0, MaxScroll);
        StartAnimation();
    }

    // ---- Animation -----------------------------------------------------------------------------

    private void StartAnimation()
    {
        if (_timer.Enabled) return;
        _lastTick = _clock.ElapsedMilliseconds;
        _timer.Start();
    }

    private void OnTick()
    {
        long now = _clock.ElapsedMilliseconds;
        float dt = Math.Min(50, Math.Max(1, now - _lastTick));
        _lastTick = now;
        bool busy = false;
        // Cursor animations redraw at most ~30 fps; easing animations still advance every tick.
        bool frameTick = now / AnimationFrameMs != _animationFrame;
        if (frameTick) _animationFrame = now / AnimationFrameMs;

        if (_scroll.Step(dt, 60))
        {
            busy = true;
            InvalidateGrid();
            if (ClientRectangle.Contains(PointToClient(MousePosition)) && !_draggingThumb) UpdateHover(PointToClient(MousePosition));
        }

        for (int i = 0; i < _cards.Count; i++)
        {
            var card = _cards[i];
            bool changed = card.Hover.Step(dt, 45) | card.Press.Step(dt, 28) | card.Selected.Step(dt, 55) |
                           card.Appear.Step(dt, 70) | card.Peek.Step(dt, 70);
            bool hovered = i == _hoverIndex && card.Pack != null;
            if (hovered && card.Peek.Target == 0)
            {
                // Reveal the other roles only after a short dwell, so sweeping across the grid stays calm.
                if (now - card.HoverStart >= PeekDelayMs) card.Peek.Target = 1;
                busy = true;
            }
            bool animating = hovered &&
                             (card.Pack.Preview?.IsAnimated == true || card.Pack.RolePreviews?.Any(p => p?.IsAnimated == true) == true);
            if (animating) busy = true;
            if (changed || (animating && frameTick))
            {
                busy = true;
                InvalidateCard(card);
            }
        }

        foreach (var chip in _chips)
        {
            if (!(chip.Hover.Step(dt, 45) | chip.Selected.Step(dt, 60))) continue;
            busy = true;
            var r = Rectangle.Round(chip.Bounds);
            r.Inflate(2, 2);
            Invalidate(r);
        }

        _divider.Target = _scroll.Value > 0.5f ? 1 : 0;
        if (_divider.Step(dt, 60))
        {
            busy = true;
            Invalidate(new Rectangle(0, ViewportTopPx - 2, ClientSize.Width, 4));
        }

        if (StepChrome(dt)) busy = true;
        if (StepCatalog(frameTick)) busy = true;

        if (_dropHover.Step(dt, 50))
        {
            busy = true;
            Invalidate();
        }

        if (_toastText != null)
        {
            busy = true;
            if (now > _toastUntil) _toastAnim.Target = 0;
            if (_toastAnim.Step(dt, 60)) InvalidateToast();
            if (_toastAnim.Value == 0 && _toastAnim.Target == 0) _toastText = null;
        }

        if (!busy) _timer.Stop();
    }

    private void InvalidateCard(Card card)
    {
        var r = Rectangle.Round(ScreenRect(card));
        r.Inflate((int)(6 * S), (int)(6 * S));
        r.Intersect(new Rectangle(0, ViewportTopPx, ClientSize.Width, ClientSize.Height - ViewportTopPx));
        if (!r.IsEmpty) Invalidate(r);
    }

    private void InvalidateGrid() => Invalidate(new Rectangle(0, ViewportTopPx - 2, ClientSize.Width, ClientSize.Height - ViewportTopPx + 2));

    private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
}
