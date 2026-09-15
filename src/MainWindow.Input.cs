using System;
using System.Drawing;
using System.Windows.Forms;

namespace Cursors;

internal sealed partial class MainWindow
{
    private const int PeekDelayMs = 260;

    private enum Zone
    {
        None,
        Card,
        Chip,
        Restore,
        Search,
        SearchClear,
        ScrollThumb,
        ScrollTrack,
    }

    private int _hoverIndex = -1, _pressIndex = -1, _focusIndex = -1;
    private bool _keyboardFocus, _draggingThumb;
    private Zone _pressZone;
    private float _thumbGrabOffset;

    private int HitTest(Point p, out Zone zone)
    {
        zone = Zone.None;
        if (p.Y < TitleBarPx)
        {
            if (RestoreButtonRect().Contains(p)) zone = Zone.Restore;
            else if (_query.Length > 0 && SearchClearRect().Contains(p)) zone = Zone.SearchClear;
            else if (SearchRect().Contains(p)) zone = Zone.Search;
            return -1;
        }
        if (p.Y < ViewportTopPx)
        {
            int chip = _chips.FindIndex(c => c.Bounds.Contains(p));
            if (chip >= 0) zone = Zone.Chip;
            return chip;
        }
        if (HasScroll && ScrollTrackRect().Contains(p))
        {
            var thumb = ScrollThumbRect();
            zone = p.Y >= thumb.Top && p.Y <= thumb.Bottom ? Zone.ScrollThumb : Zone.ScrollTrack;
            return -1;
        }
        for (int i = 0; i < _cards.Count; i++)
        {
            if (ScreenRect(_cards[i]).Contains(p))
            {
                zone = Zone.Card;
                return i;
            }
        }
        return -1;
    }

    private void UpdateHover(Point p)
    {
        int index = HitTest(p, out var zone);
        int cardIndex = zone == Zone.Card ? index : -1;
        if (cardIndex != _hoverIndex)
        {
            if (_hoverIndex >= 0 && _hoverIndex < _cards.Count)
            {
                _cards[_hoverIndex].Hover.Target = 0;
                _cards[_hoverIndex].Peek.Target = 0;
            }
            _hoverIndex = cardIndex;
            if (cardIndex >= 0)
            {
                var card = _cards[cardIndex];
                card.Hover.Target = 1;
                card.HoverStart = _clock.ElapsedMilliseconds;
                EnsureRolePreviews(card);
            }
        }
        for (int i = 0; i < _chips.Count; i++) _chips[i].Hover.Target = zone == Zone.Chip && i == index ? 1 : 0;
        _restoreHover.Target = zone == Zone.Restore ? 1 : 0;
        _searchClearHover.Target = zone == Zone.SearchClear ? 1 : 0;
        _scrollbarHover.Target = zone is Zone.ScrollThumb or Zone.ScrollTrack || _draggingThumb ? 1 : 0;
        StartAnimation();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetCaptionHover(0);
        if (_draggingThumb)
        {
            var track = ScrollTrackRect();
            float thumbH = ScrollThumbRect().Height;
            float fraction = (e.Y - _thumbGrabOffset - track.Y) / Math.Max(1, track.Height - thumbH);
            _scroll.Target = Clamp(fraction * MaxScroll, 0, MaxScroll);
            _scroll.Value = _scroll.Target;
            InvalidateGrid();
            return;
        }
        UpdateHover(e.Location);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_draggingThumb) return;
        UpdateHover(new Point(-1, -1));
        _restoreHover.Target = 0;
        _scrollbarHover.Target = 0;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        int index = HitTest(e.Location, out var zone);
        _pressZone = zone;
        _pressIndex = index;
        switch (zone)
        {
            case Zone.Card:
                _cards[index].Press.Target = 1;
                if (_focusIndex != index && _keyboardFocus) InvalidateGrid();
                _focusIndex = index;
                _keyboardFocus = false;
                break;
            case Zone.Restore:
                _restorePress.Target = 1;
                break;
            case Zone.Search:
                FocusSearch();
                break;
            case Zone.ScrollThumb:
                _draggingThumb = true;
                _thumbGrabOffset = e.Y - ScrollThumbRect().Y;
                break;
            case Zone.ScrollTrack:
                float direction = e.Y < ScrollThumbRect().Y ? -1 : 1;
                _scroll.Target = Clamp(_scroll.Target + direction * ViewportHeight * 0.9f, 0, MaxScroll);
                break;
        }
        Capture = zone != Zone.None && zone != Zone.Search;
        StartAnimation();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right)
        {
            int target = HitTest(e.Location, out var rightZone);
            if (rightZone == Zone.Card && !_cards[target].IsAdd) ShowContextMenu(_cards[target], PointToScreen(e.Location));
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        int index = HitTest(e.Location, out var zone);
        var pressZone = _pressZone;
        int pressIndex = _pressIndex;
        _pressZone = Zone.None;
        _pressIndex = -1;
        _draggingThumb = false;
        Capture = false;

        switch (pressZone)
        {
            case Zone.Card when pressIndex >= 0 && pressIndex < _cards.Count:
                _cards[pressIndex].Press.Target = 0;
                if (zone == Zone.Card && index == pressIndex) Activate(_cards[pressIndex]);
                break;
            case Zone.Chip when zone == Zone.Chip && index == pressIndex:
                SetFilter(_chips[index].Category);
                break;
            case Zone.Restore:
                _restorePress.Target = 0;
                if (zone == Zone.Restore) RestoreDefault();
                break;
            case Zone.SearchClear when zone == Zone.SearchClear:
                _search.Clear();
                FocusSearch();
                break;
        }

        if (!IsDisposed)
        {
            UpdateHover(PointToClient(MousePosition));
            StartAnimation();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!HasScroll) return;
        _scroll.Target = Clamp(_scroll.Target - e.Delta / 120f * 110 * S, 0, MaxScroll);
        StartAnimation();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (ProcessSearchKey(ref msg, keyData, out bool handled))
            return handled || base.ProcessCmdKey(ref msg, keyData);
        switch (keyData)
        {
            case Keys.Left: MoveFocus(-1, 0); return true;
            case Keys.Right: MoveFocus(1, 0); return true;
            case Keys.Up: MoveFocus(0, -1); return true;
            case Keys.Down: MoveFocus(0, 1); return true;
            case Keys.Home:
                if (_cards.Count > 0) SetFocus(0);
                return true;
            case Keys.End:
                if (_cards.Count > 0) SetFocus(_cards.Count - 1);
                return true;
            case Keys.Enter:
            case Keys.Space:
                if (_focusIndex >= 0 && _focusIndex < _cards.Count)
                {
                    _keyboardFocus = true;
                    Activate(_cards[_focusIndex]);
                }
                return true;
            case Keys.PageDown:
            case Keys.PageUp:
                _scroll.Target = Clamp(_scroll.Target + (keyData == Keys.PageDown ? 1 : -1) * ViewportHeight * 0.9f, 0, MaxScroll);
                StartAnimation();
                return true;
            case Keys.Control | Keys.Tab:
            case Keys.Control | Keys.Shift | Keys.Tab:
            {
                int current = Math.Max(0, _chips.FindIndex(c => c.Category == _filter));
                int step = (keyData & Keys.Shift) == Keys.Shift ? -1 : 1;
                if (_chips.Count > 0) SetFilter(_chips[(current + step + _chips.Count) % _chips.Count].Category);
                return true;
            }
            case Keys.Apps:
            case Keys.Shift | Keys.F10:
                if (_focusIndex >= 0 && _focusIndex < _cards.Count && !_cards[_focusIndex].IsAdd)
                {
                    var r = Rectangle.Round(ScreenRect(_cards[_focusIndex]));
                    ShowContextMenu(_cards[_focusIndex], PointToScreen(new Point(r.X + r.Width / 2, r.Y + r.Height / 2)));
                }
                return true;
            case Keys.Control | Keys.O:
                BrowseForPacks();
                return true;
            case Keys.Control | Keys.L:
                PromptImportLink();
                return true;
            case Keys.Control | Keys.V:
                string clip = null;
                try
                {
                    clip = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : null;
                }
                catch { }
                if (!LinkImport.LooksLikeLink(clip)) return base.ProcessCmdKey(ref msg, keyData);
                PromptImportLink(clip);
                return true;
            case Keys.F5:
                ReloadLibrary();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Arrow-key navigation that follows the visual grid, including across section headers.</summary>
    private void MoveFocus(int dx, int dy)
    {
        if (_cards.Count == 0) return;
        if (_focusIndex < 0 || !_keyboardFocus)
        {
            int start = _focusIndex >= 0 ? _focusIndex : Math.Max(0, _cards.FindIndex(c => c.Pack?.Id == _selectedId));
            SetFocus(start);
            return;
        }

        int next = _focusIndex;
        var current = _cards[_focusIndex].Bounds;
        if (dx != 0)
        {
            next = Math.Max(0, Math.Min(_cards.Count - 1, _focusIndex + dx));
        }
        else
        {
            float centerX = current.X + current.Width / 2, bestRow = float.MaxValue, bestColumn = float.MaxValue;
            for (int i = 0; i < _cards.Count; i++)
            {
                var b = _cards[i].Bounds;
                float rowDistance = dy > 0 ? b.Top - current.Top : current.Top - b.Top;
                if (rowDistance < 1) continue;
                float columnDistance = Math.Abs(b.X + b.Width / 2 - centerX);
                bool closerRow = rowDistance < bestRow - 1;
                bool sameRow = Math.Abs(rowDistance - bestRow) <= 1;
                if (closerRow || (sameRow && columnDistance < bestColumn))
                {
                    bestRow = rowDistance;
                    bestColumn = columnDistance;
                    next = i;
                }
            }
        }
        SetFocus(next);
    }

    private void SetFocus(int index)
    {
        if (_focusIndex >= 0 && _focusIndex < _cards.Count) InvalidateCard(_cards[_focusIndex]);
        _focusIndex = index;
        _keyboardFocus = true;
        InvalidateCard(_cards[index]);
        ScrollIntoView(index);
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        bool files = e.Data.GetDataPresent(DataFormats.FileDrop) || DroppedLink(e.Data) != null;
        e.Effect = files ? DropEffect(e) : DragDropEffects.None;
        _dropHover.Target = files ? 1 : 0;
        StartAnimation();
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) || DroppedLink(e.Data) != null ? DropEffect(e) : DragDropEffects.None;
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        _dropHover.Target = 0;
        StartAnimation();
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        _dropHover.Target = 0;
        StartAnimation();
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0) ImportAsync(paths);
        else if (DroppedLink(e.Data) is string link) ImportLinkAsync(link);
    }

    // Browsers usually offer links as Link or Copy; files from Explorer as Copy or Move.
    private static DragDropEffects DropEffect(DragEventArgs e) =>
        (e.AllowedEffect & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.Link;
}
