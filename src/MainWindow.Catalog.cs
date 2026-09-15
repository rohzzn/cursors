using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Cursors;

/// <summary>
/// Community catalog cards. Their preview images download as they come into view, a couple at a time and what's on
/// screen first. Clicking one downloads the set from its site, adds it to the library and applies it.
/// </summary>
internal sealed partial class MainWindow
{
    private const int CatalogImageWorkers = 2;

    private sealed class CatalogJob
    {
        public CursorPack Pack;
        public string ImageId;
        public int Generation, Box;
    }

    private readonly List<CatalogJob> _catalogQueue = new(); // locked; the newest request is served first
    private readonly HashSet<string> _catalogQueued = new(), _catalogFailed = new(), _catalogDownloading = new();
    private volatile HashSet<string> _catalogNearView = new(); // replaced, never modified, so workers can read it
    private int _catalogWorkers; // guarded by _catalogQueue
    private readonly HashSet<CursorPack> _catalogShown = new(); // community packs holding a decoded preview
    private const int MaxCatalogPreviews = 400;

    private void ResetCatalogQueue()
    {
        lock (_catalogQueue) _catalogQueue.Clear();
        _catalogQueued.Clear();
        _catalogFailed.Clear();
    }

    /// <summary>Queues preview downloads for Community cards on or near the screen that don't have one yet.</summary>
    private void RequestVisibleCatalogPreviews()
    {
        float margin = ViewportHeight * 0.5f, top = ViewportTop - margin, bottom = ClientSize.Height + margin;
        int box = (int)Math.Round(PreviewBox * S);
        var near = new HashSet<string>();
        // Bottom to top, so the queue (served newest first) starts with the top of the screen.
        VisibleRange(margin, out int firstNear, out int lastNear);
        for (int i = lastNear; i >= firstNear; i--)
        {
            var pack = _cards[i].Pack;
            if (pack == null || pack.Kind != PackKind.Catalog || pack.Preview != null) continue;
            var r = ScreenRect(_cards[i]);
            string id = pack.CatalogPreviews?[0];
            if (r.Bottom < top || r.Top > bottom || id == null) continue;
            near.Add(id);
            if (_catalogFailed.Contains(id) || !_catalogQueued.Add(id)) continue;
            lock (_catalogQueue) _catalogQueue.Add(new CatalogJob { Pack = pack, ImageId = id, Generation = _previewGeneration, Box = box });
            PumpCatalogWorkers();
        }
        _catalogNearView = near;
    }

    private void PumpCatalogWorkers()
    {
        lock (_catalogQueue)
        {
            if (_catalogWorkers >= CatalogImageWorkers || _catalogQueue.Count <= _catalogWorkers) return;
            _catalogWorkers++;
        }
        ThreadPool.QueueUserWorkItem(_ => RunCatalogWorker());
    }

    private void RunCatalogWorker()
    {
        while (true)
        {
            CatalogJob job;
            lock (_catalogQueue)
            {
                if (_catalogQueue.Count == 0)
                {
                    _catalogWorkers--;
                    return;
                }
                job = _catalogQueue[_catalogQueue.Count - 1];
                _catalogQueue.RemoveAt(_catalogQueue.Count - 1);
            }

            bool posted;
            if (job.Generation != _previewGeneration || !_catalogNearView.Contains(job.ImageId))
            {
                // Scrolled away before its turn; it's queued again if it comes back into view.
                posted = PostToWindow(() =>
                {
                    if (job.Generation == _previewGeneration) _catalogQueued.Remove(job.ImageId);
                });
            }
            else
            {
                string path = CommunityCatalog.FetchImage(job.ImageId);
                var preview = path == null ? null : CursorDecoder.CreateImagePreview(path, job.Box);
                posted = PostToWindow(() => OnCatalogPreview(job, path, preview));
                if (!posted) preview?.Dispose();
            }
            if (posted) continue;
            lock (_catalogQueue)
            {
                _catalogQueue.Clear();
                _catalogWorkers--;
            }
            return;
        }
    }

    private void OnCatalogPreview(CatalogJob job, string path, CursorPreview preview)
    {
        if (job.Generation != _previewGeneration)
        {
            if (preview != null) _stalePreviews.Add(preview);
            return;
        }
        _catalogQueued.Remove(job.ImageId);
        if (preview == null)
        {
            _catalogFailed.Add(job.ImageId);
            if (_cardByPack.TryGetValue(job.Pack, out var card)) InvalidateCard(card);
            return;
        }
        OnPreviewReady(job.Generation, job.Box, path, job.Pack, preview);
        if (job.Pack.Preview != null) _catalogShown.Add(job.Pack);
        TrimCatalogPreviews();
    }

    /// <summary>
    /// Scrolling through thousands of community sets would keep every preview in memory; past a limit, previews of
    /// cards far from the screen are released. They come back from the disk cache when those cards return.
    /// </summary>
    private void TrimCatalogPreviews()
    {
        if (_catalogShown.Count <= MaxCatalogPreviews) return;
        VisibleRange(ViewportHeight, out int first, out int last);
        var keep = new HashSet<CursorPack>();
        for (int i = first; i <= last; i++)
            if (_cards[i].Pack != null) keep.Add(_cards[i].Pack);

        foreach (var pack in _catalogShown.ToList())
        {
            if (_catalogShown.Count <= MaxCatalogPreviews * 3 / 4) break;
            if (keep.Contains(pack)) continue;
            _catalogShown.Remove(pack);
            var preview = pack.Preview;
            if (preview == null) continue;
            pack.Preview = null;
            foreach (string key in _previewCache.Where(kv => kv.Value == preview).Select(kv => kv.Key).ToList()) _previewCache.Remove(key);
            _stalePreviews.Remove(preview);
            preview.Dispose();
            DropRolePreviews(pack);
            if (_cardByPack.TryGetValue(pack, out var card)) card.Appear.Snap(0);
        }
    }

    private bool PostToWindow(Action action)
    {
        try
        {
            if (IsDisposed || !IsHandleCreated) return false;
            BeginInvoke(action);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // ---- Download and apply --------------------------------------------------------------------

    private void DownloadCatalogPack(Card card)
    {
        var entry = card.Pack;
        if (!_catalogDownloading.Add(entry.Id))
        {
            ShowToast($"Still downloading “{entry.Label}”");
            return;
        }
        InvalidateCard(card);
        StartAnimation();
        ShowToast($"Downloading “{entry.Label}”…");
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var result = LinkImport.Import(entry.Url, entry.Author);
            PostToWindow(() => OnCatalogDownloaded(entry, result));
        });
    }

    private void OnCatalogDownloaded(CursorPack entry, LinkImportResult result)
    {
        _catalogDownloading.Remove(entry.Id);
        if (_cardByPack.TryGetValue(entry, out var card)) InvalidateCard(card);
        if (result.Error != null)
        {
            ShowToast(result.Error == "That link wasn't found" ? $"“{entry.Label}” is no longer available" : result.Error);
            return;
        }
        string id = result.Import.AddedIds.FirstOrDefault() ?? result.Import.ExistingIds.FirstOrDefault();
        if (id == null)
        {
            ShowToast($"No cursors found in “{entry.Label}”");
            return;
        }

        ReloadLibrary(result.Import.AddedIds);
        var pack = _library.Packs.FirstOrDefault(p => p.Id == id);
        if (pack == null) return;
        SetSelected(pack.Id);
        _applier.Request(pack, _library.WindowsDefault);
        ShowToast(pack.License != null ? $"Applied “{pack.Label}” · {pack.License}" : $"Applied “{pack.Label}”");
    }

    /// <summary>Keeps the spinner turning on cards whose set is downloading.</summary>
    private bool StepCatalog(bool frameTick)
    {
        if (_catalogDownloading.Count == 0) return false;
        if (frameTick)
            foreach (var card in _cards)
                if (card.Pack != null && _catalogDownloading.Contains(card.Pack.Id)) InvalidateCard(card);
        return true;
    }

    // ---- Drawing -------------------------------------------------------------------------------

    /// <summary>A download mark on hover, a spinner while downloading, and a quiet note when the preview is unavailable.</summary>
    private void DrawCatalogExtras(Graphics g, Card card, RectangleF r, float areaH, long now)
    {
        var pack = card.Pack;
        float s = S;
        if (pack.Preview == null && pack.CatalogPreviews?[0] is string id && _catalogFailed.Contains(id))
        {
            var area = Rectangle.Round(new RectangleF(r.X + 12 * s, r.Y + 6 * s, r.Width - 24 * s, areaH));
            TextRenderer.DrawText(g, "No preview", _fonts.Label, area, Theme.TextInactive, CenteredText);
        }

        bool downloading = _catalogDownloading.Contains(pack.Id);
        float t = downloading ? 1 : Theme.EaseOut(card.Hover.Value);
        if (t < 0.02f) return;
        float d = 16 * s, cx = r.Right - 19 * s, cy = r.Y + 19 * s;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (downloading)
        {
            var circle = new RectangleF(cx - d / 2, cy - d / 2, d, d);
            using var track = new Pen(Color.FromArgb(46, 255, 255, 255), 1.6f * s);
            using var arc = new Pen(Theme.Text, 1.6f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawEllipse(track, circle);
            g.DrawArc(arc, circle, now % 900 / 900f * 360, 110);
            return;
        }
        // Not downloaded yet: an arrow into a tray.
        using var pen = new Pen(Theme.Fade(Theme.TextSecondary, t), 1.5f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float tip = cy + 2.5f * s;
        g.DrawLine(pen, cx, cy - 5.5f * s, cx, tip);
        g.DrawLines(pen, new[] { new PointF(cx - 3.5f * s, tip - 3.5f * s), new PointF(cx, tip), new PointF(cx + 3.5f * s, tip - 3.5f * s) });
        g.DrawLine(pen, cx - 5.5f * s, cy + 6 * s, cx + 5.5f * s, cy + 6 * s);
    }
}
