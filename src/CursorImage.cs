using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Cursors;

/// <summary>Pre-scaled preview frames for a .cur or .ani file.</summary>
internal sealed class CursorPreview : IDisposable
{
    public Bitmap[] Frames { get; }
    public int[] Sequence { get; }
    public int[] DurationsMs { get; }
    public int TotalMs { get; }
    public bool IsAnimated => Sequence.Length > 1;

    public CursorPreview(Bitmap[] frames, int[] sequence, int[] durations)
    {
        Frames = frames;
        Sequence = sequence;
        DurationsMs = durations;
        int total = 0;
        foreach (int d in durations) total += d;
        TotalMs = Math.Max(1, total);
    }

    public Bitmap FrameAt(long elapsedMs)
    {
        if (!IsAnimated) return Frames[0];
        long t = elapsedMs % TotalMs;
        for (int i = 0; i < Sequence.Length; i++)
        {
            t -= DurationsMs[i];
            if (t < 0) return Frames[Sequence[i]];
        }
        return Frames[Sequence[Sequence.Length - 1]];
    }

    public void Dispose()
    {
        foreach (var f in Frames) f.Dispose();
    }
}

/// <summary>Minimal, dependency-free decoder for Windows cursor files (ICO/CUR containers and RIFF ACON animations).</summary>
internal static class CursorDecoder
{
    private sealed class RawImage
    {
        public int Width, Height;
        public int[] Pixels; // straight (non-premultiplied) ARGB
    }

    // Colour used for "invert screen" pixels of monochrome cursors, as they would appear on a dark surface.
    private const int InvertColor = unchecked((int)0xFFE4E4E4);

    public static CursorPreview CreatePreview(string path, int boxPx)
    {
        try
        {
            byte[] data = File.ReadAllBytes(path);
            var frames = new List<RawImage>();
            int[] sequence, durations;

            if (data.Length >= 12 && Ascii(data, 0) == "RIFF" && Ascii(data, 8) == "ACON")
            {
                if (!ParseAni(data, boxPx, frames, out sequence, out durations)) return null;
            }
            else
            {
                var img = DecodeIconGroup(data, 0, data.Length, boxPx);
                if (img == null) return null;
                frames.Add(img);
                sequence = new[] { 0 };
                durations = new[] { 1000 };
            }

            return Render(frames, sequence, durations, boxPx);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Preview of a plain image, such as a cursor site's PNG rendering, scaled like a cursor file's preview.</summary>
    public static CursorPreview CreateImagePreview(string path, int boxPx)
    {
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var source = new Bitmap(stream);
            using var argb = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
            var img = new RawImage { Width = argb.Width, Height = argb.Height, Pixels = new int[argb.Width * argb.Height] };
            var bd = argb.LockBits(new Rectangle(0, 0, argb.Width, argb.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < img.Height; y++)
                    Marshal.Copy(bd.Scan0 + y * bd.Stride, img.Pixels, y * img.Width, img.Width);
            }
            finally
            {
                argb.UnlockBits(bd);
            }
            RemoveHotspotMark(img);
            return Render(new List<RawImage> { img }, new[] { 0 }, new[] { 1000 }, boxPx);
        }
        catch
        {
            return null;
        }
    }

    // rw-designer's cursor previews mark the hotspot with a dotted gray cross: #7F7F7F dots 2, 4, 6 and 8 px from it
    // along each axis, fading out, drawn where the cursor is transparent. It isn't part of the cursor, so it goes.
    private static readonly int[] HotspotMarkAlpha = { 0xFF, 0xFF, 0x9F, 0x3E };

    private static void RemoveHotspotMark(RawImage img)
    {
        int bestScore = 0, bestX = 0, bestY = 0;
        for (int y = 0; y < img.Height; y++)
        {
            for (int x = 0; x < img.Width; x++)
            {
                int score = 0;
                for (int i = 0; i < HotspotMarkAlpha.Length; i++)
                {
                    int d = 2 * (i + 1), a = HotspotMarkAlpha[i];
                    score += IsMarkDot(img, x - d, y, a) + IsMarkDot(img, x + d, y, a) + IsMarkDot(img, x, y - d, a) + IsMarkDot(img, x, y + d, a);
                }
                if (score <= bestScore) continue;
                bestScore = score;
                bestX = x;
                bestY = y;
            }
        }
        if (bestScore < 3) return;
        for (int i = 0; i < HotspotMarkAlpha.Length; i++)
        {
            int d = 2 * (i + 1);
            FixMarkDot(img, bestX - d, bestY, 0, 1);
            FixMarkDot(img, bestX + d, bestY, 0, 1);
            FixMarkDot(img, bestX, bestY - d, 1, 0);
            FixMarkDot(img, bestX, bestY + d, 1, 0);
        }
    }

    /// <summary>
    /// The cursor is drawn over the mark, so a dot shows as more coverage than the two pixels beside it across the arm.
    /// Such a pixel is rebuilt from that pair: a bare dot becomes transparent, a dot under a glow takes the glow's color.
    /// Pixels the mark doesn't show through, such as opaque outlines, are left alone.
    /// </summary>
    private static void FixMarkDot(RawImage img, int x, int y, int acrossX, int acrossY)
    {
        if (x < 0 || y < 0 || x >= img.Width || y >= img.Height) return;
        uint pixel = (uint)img.Pixels[y * img.Width + x];
        uint a = PixelOrClear(img, x - acrossX, y - acrossY), b = PixelOrClear(img, x + acrossX, y + acrossY);
        if (pixel >> 24 <= Math.Max(a >> 24, b >> 24) + 8) return;
        uint mixed = 0;
        for (int shift = 0; shift < 32; shift += 8)
            mixed |= ((((a >> shift) & 0xFF) + ((b >> shift) & 0xFF)) / 2) << shift;
        img.Pixels[y * img.Width + x] = unchecked((int)mixed);
    }

    private static uint PixelOrClear(RawImage img, int x, int y) =>
        x < 0 || y < 0 || x >= img.Width || y >= img.Height ? 0u : (uint)img.Pixels[y * img.Width + x];

    private static int IsMarkDot(RawImage img, int x, int y, int alpha) =>
        x >= 0 && y >= 0 && x < img.Width && y < img.Height && img.Pixels[y * img.Width + x] == unchecked((int)((uint)alpha << 24 | 0x7F7F7F)) ? 1 : 0;

    private static CursorPreview Render(List<RawImage> frames, int[] sequence, int[] durations, int boxPx)
    {
        // Union of visible bounds across all frames, in canvas fractions, so animations don't jitter.
        double fx0 = 1, fy0 = 1, fx1 = 0, fy1 = 0;
        foreach (var f in frames)
        {
            int minX = f.Width, minY = f.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < f.Height; y++)
            {
                int row = y * f.Width;
                for (int x = 0; x < f.Width; x++)
                {
                    if ((uint)f.Pixels[row + x] >> 24 <= 12) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < 0) continue;
            fx0 = Math.Min(fx0, (double)minX / f.Width);
            fy0 = Math.Min(fy0, (double)minY / f.Height);
            fx1 = Math.Max(fx1, (double)(maxX + 1) / f.Width);
            fy1 = Math.Max(fy1, (double)(maxY + 1) / f.Height);
        }
        if (fx1 <= fx0 || fy1 <= fy0) { fx0 = fy0 = 0; fx1 = fy1 = 1; }

        var reference = frames[0];
        double bw = (fx1 - fx0) * reference.Width, bh = (fy1 - fy0) * reference.Height;
        double scale = boxPx / Math.Max(bw, bh);
        int outW = Math.Max(1, (int)Math.Round(bw * scale));
        int outH = Math.Max(1, (int)Math.Round(bh * scale));

        var bitmaps = new Bitmap[frames.Count];
        using var attrs = new ImageAttributes();
        attrs.SetWrapMode(WrapMode.TileFlipXY);

        for (int i = 0; i < frames.Count; i++)
        {
            var f = frames[i];
            using var src = ToBitmap(f);
            var dst = new Bitmap(outW, outH, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                float sx = (float)(fx0 * f.Width), sy = (float)(fy0 * f.Height);
                float sw = (float)((fx1 - fx0) * f.Width), sh = (float)((fy1 - fy0) * f.Height);
                g.DrawImage(src, new Rectangle(0, 0, outW, outH), sx, sy, sw, sh, GraphicsUnit.Pixel, attrs);
            }
            bitmaps[i] = dst;
        }
        return new CursorPreview(bitmaps, sequence, durations);
    }

    private static Bitmap ToBitmap(RawImage img)
    {
        var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < img.Height; y++)
                Marshal.Copy(img.Pixels, y * img.Width, bd.Scan0 + y * bd.Stride, img.Width);
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
        return bmp;
    }

    private static bool ParseAni(byte[] d, int boxPx, List<RawImage> frames, out int[] sequence, out int[] durations)
    {
        sequence = null;
        durations = null;
        int nSteps = 0, dispRate = 6;
        int[] rates = null, seq = null;
        var icons = new List<KeyValuePair<int, int>>();

        void Walk(int start, int end)
        {
            int p = start;
            while (p + 8 <= end)
            {
                string id = Ascii(d, p);
                int len = BitConverter.ToInt32(d, p + 4);
                int body = p + 8;
                if (len < 0 || body + len > end) len = end - body;
                switch (id)
                {
                    case "LIST":
                        if (len >= 4) Walk(body + 4, body + len);
                        break;
                    case "anih":
                        if (len >= 36)
                        {
                            nSteps = BitConverter.ToInt32(d, body + 8);
                            dispRate = BitConverter.ToInt32(d, body + 28);
                        }
                        break;
                    case "rate":
                        rates = ReadInts(d, body, len / 4);
                        break;
                    case "seq ":
                        seq = ReadInts(d, body, len / 4);
                        break;
                    case "icon":
                        icons.Add(new KeyValuePair<int, int>(body, len));
                        break;
                }
                p = body + len + (len & 1);
            }
        }

        int riffEnd = Math.Min(d.Length, 8 + Math.Max(0, BitConverter.ToInt32(d, 4)));
        Walk(12, riffEnd);
        if (icons.Count == 0) return false;

        var map = new int[icons.Count];
        for (int i = 0; i < icons.Count; i++)
        {
            var img = DecodeIconGroup(d, icons[i].Key, icons[i].Value, boxPx);
            if (img == null)
            {
                map[i] = -1;
                continue;
            }
            map[i] = frames.Count;
            frames.Add(img);
        }
        if (frames.Count == 0) return false;

        int steps = seq?.Length ?? (rates?.Length ?? (nSteps > 0 ? nSteps : icons.Count));
        var outSeq = new List<int>(steps);
        var outDur = new List<int>(steps);
        for (int i = 0; i < steps; i++)
        {
            int src = seq != null ? seq[i] : i;
            int idx = src >= 0 && src < map.Length ? map[src] : -1;
            if (idx < 0) continue;
            int jiffies = rates != null && i < rates.Length ? rates[i] : dispRate;
            outSeq.Add(idx);
            outDur.Add(Math.Max(16, jiffies * 1000 / 60));
        }
        if (outSeq.Count == 0)
        {
            outSeq.Add(0);
            outDur.Add(1000);
        }
        sequence = outSeq.ToArray();
        durations = outDur.ToArray();
        return true;
    }

    /// <summary>Picks the best image in an ICO/CUR directory for the preview size and decodes it.</summary>
    private static RawImage DecodeIconGroup(byte[] d, int offset, int length, int boxPx)
    {
        if (length < 6 || offset + length > d.Length) return null;
        int type = BitConverter.ToUInt16(d, offset + 2);
        int count = BitConverter.ToUInt16(d, offset + 4);
        if ((type != 1 && type != 2) || count == 0 || 6 + count * 16 > length) return null;

        int need = (int)(boxPx * 1.5);
        int best = -1, bestSize = 0, bestDepth = 0;
        bool bestFits = false;
        for (int i = 0; i < count; i++)
        {
            int e = offset + 6 + i * 16;
            int w = d[e] == 0 ? 256 : d[e];
            int h = d[e + 1] == 0 ? 256 : d[e + 1];
            int size = Math.Max(w, h);
            int imgOff = offset + BitConverter.ToInt32(d, e + 12);
            int imgLen = BitConverter.ToInt32(d, e + 8);
            if (imgOff < offset || imgLen <= 0 || imgOff + Math.Min(imgLen, 16) > d.Length) continue;
            int depth = IsPng(d, imgOff) ? 32 : (imgOff + 16 <= d.Length ? BitConverter.ToUInt16(d, imgOff + 14) : 0);
            bool fits = size >= need;

            bool better;
            if (best < 0) better = true;
            else if (fits != bestFits) better = fits;
            else if (size != bestSize) better = fits ? size < bestSize : size > bestSize;
            else better = depth > bestDepth;

            if (better)
            {
                best = i;
                bestSize = size;
                bestDepth = depth;
                bestFits = fits;
            }
        }
        if (best < 0) return null;

        int be = offset + 6 + best * 16;
        int off = offset + BitConverter.ToInt32(d, be + 12);
        int len = Math.Min(BitConverter.ToInt32(d, be + 8), d.Length - off);
        return IsPng(d, off) ? DecodePng(d, off, len) : DecodeDib(d, off, len);
    }

    private static RawImage DecodePng(byte[] d, int off, int len)
    {
        using var ms = new MemoryStream(d, off, len, false);
        using var png = new Bitmap(ms);
        int w = png.Width, h = png.Height;
        using var argb = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(argb))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(png, 0, 0, w, h);
        }
        var img = new RawImage { Width = w, Height = h, Pixels = new int[w * h] };
        var bd = argb.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < h; y++)
                Marshal.Copy(bd.Scan0 + y * bd.Stride, img.Pixels, y * w, w);
        }
        finally
        {
            argb.UnlockBits(bd);
        }
        return img;
    }

    private static RawImage DecodeDib(byte[] d, int off, int len)
    {
        int end = off + len;
        if (len < 40) return null;
        int headerSize = BitConverter.ToInt32(d, off);
        if (headerSize < 40) return null;
        int w = BitConverter.ToInt32(d, off + 4);
        int fullH = BitConverter.ToInt32(d, off + 8);
        int bpp = BitConverter.ToUInt16(d, off + 14);
        int compression = BitConverter.ToInt32(d, off + 16);
        int clrUsed = BitConverter.ToInt32(d, off + 32);
        bool bottomUp = fullH > 0;
        int h = Math.Abs(fullH) / 2;
        if (w <= 0 || h <= 0 || w > 1024 || h > 1024) return null;
        if (bpp != 1 && bpp != 4 && bpp != 8 && bpp != 24 && bpp != 32) return null;
        if (compression != 0 && compression != 3) return null;

        int palOff = off + headerSize + (compression == 3 && headerSize == 40 ? 12 : 0);
        int palCount = bpp <= 8 ? (clrUsed > 0 && clrUsed <= 256 ? clrUsed : 1 << bpp) : 0;
        int xorOff = palOff + palCount * 4;
        int xorStride = ((w * bpp + 31) / 32) * 4;
        int andOff = xorOff + xorStride * h;
        int andStride = ((w + 31) / 32) * 4;
        if (xorOff + xorStride * h > end) return null;
        bool hasAnd = andOff + andStride * h <= end;

        var px = new int[w * h];
        bool anyAlpha = false;

        for (int y = 0; y < h; y++)
        {
            int srcRow = bottomUp ? h - 1 - y : y;
            int xr = xorOff + srcRow * xorStride;
            int ar = andOff + srcRow * andStride;
            int dst = y * w;
            for (int x = 0; x < w; x++)
            {
                int b, g, r, a = 255;
                switch (bpp)
                {
                    case 32:
                        b = d[xr + x * 4]; g = d[xr + x * 4 + 1]; r = d[xr + x * 4 + 2]; a = d[xr + x * 4 + 3];
                        if (a != 0) anyAlpha = true;
                        break;
                    case 24:
                        b = d[xr + x * 3]; g = d[xr + x * 3 + 1]; r = d[xr + x * 3 + 2];
                        break;
                    default:
                        int idx = bpp == 8 ? d[xr + x]
                            : bpp == 4 ? (d[xr + x / 2] >> (x % 2 == 0 ? 4 : 0)) & 0xF
                            : (d[xr + x / 8] >> (7 - x % 8)) & 1;
                        if (idx >= palCount) idx = 0;
                        int pe = palOff + idx * 4;
                        b = d[pe]; g = d[pe + 1]; r = d[pe + 2];
                        break;
                }

                bool masked = hasAnd && ((d[ar + x / 8] >> (7 - x % 8)) & 1) == 1;
                if (bpp == 32)
                {
                    px[dst + x] = (a << 24) | (r << 16) | (g << 8) | b;
                }
                else if (masked)
                {
                    px[dst + x] = (r | g | b) == 0 ? 0 : InvertColor;
                }
                else
                {
                    px[dst + x] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                }
            }
        }

        if (bpp == 32 && !anyAlpha)
        {
            // Legacy 32bpp image without alpha: transparency comes from the AND mask.
            for (int y = 0; y < h; y++)
            {
                int ar = andOff + (bottomUp ? h - 1 - y : y) * andStride;
                for (int x = 0; x < w; x++)
                {
                    bool masked = hasAnd && ((d[ar + x / 8] >> (7 - x % 8)) & 1) == 1;
                    int rgb = px[y * w + x] & 0xFFFFFF;
                    px[y * w + x] = masked ? (rgb == 0 ? 0 : InvertColor) : unchecked((int)0xFF000000) | rgb;
                }
            }
        }

        return new RawImage { Width = w, Height = h, Pixels = px };
    }

    private static bool IsPng(byte[] d, int off) =>
        off + 8 <= d.Length && d[off] == 0x89 && d[off + 1] == 0x50 && d[off + 2] == 0x4E && d[off + 3] == 0x47;

    private static string Ascii(byte[] d, int off) => off + 4 <= d.Length ? Encoding.ASCII.GetString(d, off, 4) : "";

    private static int[] ReadInts(byte[] d, int off, int count)
    {
        var result = new int[count];
        for (int i = 0; i < count; i++) result[i] = BitConverter.ToInt32(d, off + i * 4);
        return result;
    }
}
