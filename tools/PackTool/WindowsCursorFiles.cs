using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace Cursors.PackTool;

/// <summary>Lossless, byte-level access to .cur/.ani files: read entries, drop sizes, merge sizes, rewrite.</summary>
internal static class WindowsCursorFiles
{
    public sealed class Entry
    {
        public int Width, Height, HotX, HotY;
        public byte[] Data;

        public bool IsPng => Data.Length >= 8 && Data[0] == 0x89 && Data[1] == 0x50 && Data[2] == 0x4E && Data[3] == 0x47;
    }

    public sealed class AniFile
    {
        public List<List<Entry>> Frames = new();
        public int[] Rates, Seq;
        public int DefaultRate = 6;
        public byte[] InfoChunk; // raw "LIST....INFO..." chunk, kept for author/title metadata
    }

    public static bool IsAni(byte[] d) => d.Length >= 12 && Ascii(d, 0) == "RIFF" && Ascii(d, 8) == "ACON";

    public static List<Entry> ReadGroup(byte[] d, int off, int len)
    {
        if (len < 6 || off + len > d.Length) throw new InvalidDataException("icon directory truncated");
        int type = U16(d, off + 2), count = U16(d, off + 4);
        if ((type != 1 && type != 2) || count == 0 || 6 + count * 16 > len) throw new InvalidDataException("not an ICO/CUR directory");
        var list = new List<Entry>(count);
        for (int i = 0; i < count; i++)
        {
            int e = off + 6 + i * 16;
            int size = I32(d, e + 8), pos = off + I32(d, e + 12);
            if (size <= 0 || pos < off || pos + size > off + len) throw new InvalidDataException($"entry {i} points outside the file");
            var entry = new Entry
            {
                Width = d[e] == 0 ? 256 : d[e],
                Height = d[e + 1] == 0 ? 256 : d[e + 1],
                HotX = type == 2 ? U16(d, e + 4) : 0,
                HotY = type == 2 ? U16(d, e + 6) : 0,
                Data = new byte[size],
            };
            Buffer.BlockCopy(d, pos, entry.Data, 0, size);
            // Windows picks sizes by the directory entry, so that is what Width means here. A directory byte of 0 is
            // ambiguous (256 or "unset"), so resolve it from the image header in that case only.
            if (d[e] == 0 && entry.IsPng && size >= 24)
            {
                entry.Width = BE32(entry.Data, 16);
                entry.Height = BE32(entry.Data, 20);
            }
            else if (d[e] == 0 && !entry.IsPng && size >= 16)
            {
                entry.Width = I32(entry.Data, 4);
                entry.Height = Math.Abs(I32(entry.Data, 8)) / 2;
            }
            list.Add(entry);
        }
        return list;
    }

    public static AniFile ReadAni(byte[] d)
    {
        var ani = new AniFile();
        void Walk(int start, int end)
        {
            int p = start;
            while (p + 8 <= end)
            {
                string id = Ascii(d, p);
                int len = I32(d, p + 4);
                int body = p + 8;
                if (len < 0 || body + len > end) len = end - body;
                switch (id)
                {
                    case "LIST":
                        if (len >= 4 && Ascii(d, body) == "INFO")
                        {
                            ani.InfoChunk = new byte[8 + len + (len & 1)];
                            Buffer.BlockCopy(d, p, ani.InfoChunk, 0, Math.Min(ani.InfoChunk.Length, d.Length - p));
                        }
                        else if (len >= 4)
                        {
                            Walk(body + 4, body + len);
                        }
                        break;
                    case "anih":
                        if (len >= 36) ani.DefaultRate = I32(d, body + 28);
                        break;
                    case "rate":
                        ani.Rates = Ints(d, body, len / 4);
                        break;
                    case "seq ":
                        ani.Seq = Ints(d, body, len / 4);
                        break;
                    case "icon":
                        ani.Frames.Add(ReadGroup(d, body, len));
                        break;
                }
                p = body + len + (len & 1);
            }
        }
        Walk(12, Math.Min(d.Length, 8 + I32(d, 4)));
        if (ani.Frames.Count == 0) throw new InvalidDataException("animated cursor has no frames");
        return ani;
    }

    public static byte[] WriteGroup(IEnumerable<Entry> entries)
    {
        var list = entries.OrderByDescending(e => e.Width).ToList();
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)0);
        bw.Write((ushort)2);
        bw.Write((ushort)list.Count);
        int offset = 6 + 16 * list.Count;
        foreach (var e in list)
        {
            bw.Write((byte)(e.Width >= 256 ? 0 : e.Width));
            bw.Write((byte)(e.Height >= 256 ? 0 : e.Height));
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((ushort)e.HotX);
            bw.Write((ushort)e.HotY);
            bw.Write(e.Data.Length);
            bw.Write(offset);
            offset += e.Data.Length;
        }
        foreach (var e in list) bw.Write(e.Data);
        bw.Flush();
        return ms.ToArray();
    }

    public static byte[] WriteAni(AniFile ani)
    {
        var frames = ani.Frames.Select(WriteGroup).ToList();
        int steps = ani.Seq?.Length ?? ani.Rates?.Length ?? frames.Count;
        using var body = new MemoryStream();
        using var bw = new BinaryWriter(body);
        bw.Write(Encoding.ASCII.GetBytes("ACON"));
        if (ani.InfoChunk != null) bw.Write(ani.InfoChunk);

        bw.Write(Encoding.ASCII.GetBytes("anih"));
        bw.Write(36);
        bw.Write(36);
        bw.Write(frames.Count);
        bw.Write(steps);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(Math.Max(1, ani.DefaultRate));
        bw.Write(1 | (ani.Seq != null ? 2 : 0));

        if (ani.Rates != null)
        {
            bw.Write(Encoding.ASCII.GetBytes("rate"));
            bw.Write(ani.Rates.Length * 4);
            foreach (int r in ani.Rates) bw.Write(r);
        }
        if (ani.Seq != null)
        {
            bw.Write(Encoding.ASCII.GetBytes("seq "));
            bw.Write(ani.Seq.Length * 4);
            foreach (int s in ani.Seq) bw.Write(s);
        }

        bw.Write(Encoding.ASCII.GetBytes("LIST"));
        bw.Write(4 + frames.Sum(f => 8 + f.Length + (f.Length & 1)));
        bw.Write(Encoding.ASCII.GetBytes("fram"));
        foreach (var f in frames)
        {
            bw.Write(Encoding.ASCII.GetBytes("icon"));
            bw.Write(f.Length);
            bw.Write(f);
            if ((f.Length & 1) == 1) bw.Write((byte)0);
        }
        bw.Flush();

        using var file = new MemoryStream();
        using var fw = new BinaryWriter(file);
        fw.Write(Encoding.ASCII.GetBytes("RIFF"));
        fw.Write((int)body.Length);
        fw.Write(body.ToArray());
        fw.Flush();
        return file.ToArray();
    }

    /// <summary>Keeps entries whose width is listed; returns the original bytes when nothing would be dropped.</summary>
    public static byte[] TrimSizes(byte[] file, ICollection<int> keep)
    {
        if (IsAni(file))
        {
            var ani = ReadAni(file);
            bool changed = false;
            ani.Frames = ani.Frames.Select(f =>
            {
                var kept = Filter(f, keep);
                changed |= kept.Count != f.Count;
                return kept;
            }).ToList();
            return changed ? WriteAni(ani) : file;
        }
        var entries = ReadGroup(file, 0, file.Length);
        var chosen = Filter(entries, keep);
        return chosen.Count == entries.Count ? file : WriteGroup(chosen);
    }

    private static List<Entry> Filter(List<Entry> entries, ICollection<int> keep)
    {
        var chosen = entries.Where(e => keep.Contains(e.Width)).ToList();
        return chosen.Count > 0 ? chosen : entries;
    }

    /// <summary>Adds sizes from sibling files (same picture, different size) that the primary file lacks.</summary>
    public static byte[] MergeSizes(byte[] primary, IEnumerable<byte[]> siblings)
    {
        if (IsAni(primary))
        {
            var ani = ReadAni(primary);
            foreach (var s in siblings)
            {
                if (!IsAni(s)) continue;
                var other = ReadAni(s);
                if (other.Frames.Count != ani.Frames.Count) continue;
                for (int i = 0; i < ani.Frames.Count; i++) AddMissing(ani.Frames[i], other.Frames[i]);
            }
            return WriteAni(ani);
        }
        var entries = ReadGroup(primary, 0, primary.Length);
        foreach (var s in siblings)
            if (!IsAni(s)) AddMissing(entries, ReadGroup(s, 0, s.Length));
        return WriteGroup(entries);
    }

    private static void AddMissing(List<Entry> into, List<Entry> from)
    {
        foreach (var e in from)
            if (into.All(x => x.Width != e.Width)) into.Add(e);
    }

    /// <summary>Every image in the file as frames of entries (a static cursor is a single frame).</summary>
    public static List<List<Entry>> ReadFrames(byte[] file) =>
        IsAni(file) ? ReadAni(file).Frames : new List<List<Entry>> { ReadGroup(file, 0, file.Length) };

    public static CursorImage Decode(Entry e)
    {
        if (e.IsPng)
        {
            using var ms = new MemoryStream(e.Data);
            using var bmp = new Bitmap(ms);
            return CursorImage.FromBitmap(bmp, e.HotX, e.HotY);
        }

        byte[] d = e.Data;
        int headerSize = I32(d, 0), w = I32(d, 4), fullH = I32(d, 8), bpp = U16(d, 14), clrUsed = I32(d, 32);
        int h = Math.Abs(fullH) / 2;
        bool bottomUp = fullH > 0;
        if (w <= 0 || h <= 0) throw new InvalidDataException("bad bitmap size");
        int palCount = bpp <= 8 ? (clrUsed > 0 && clrUsed <= 256 ? clrUsed : 1 << bpp) : 0;
        int palOff = headerSize, xorOff = palOff + palCount * 4;
        int xorStride = ((w * bpp + 31) / 32) * 4, andOff = xorOff + xorStride * h, andStride = ((w + 31) / 32) * 4;
        if (xorOff + xorStride * h > d.Length) throw new InvalidDataException("bitmap data truncated");
        bool hasAnd = andOff + andStride * h <= d.Length;

        var img = new CursorImage(w, h) { HotX = e.HotX, HotY = e.HotY };
        bool anyAlpha = false;
        for (int y = 0; y < h; y++)
        {
            int row = bottomUp ? h - 1 - y : y;
            int xr = xorOff + row * xorStride, ar = andOff + row * andStride;
            for (int x = 0; x < w; x++)
            {
                int b, g, r, a = 255;
                if (bpp == 32)
                {
                    b = d[xr + x * 4]; g = d[xr + x * 4 + 1]; r = d[xr + x * 4 + 2]; a = d[xr + x * 4 + 3];
                    anyAlpha |= a != 0;
                }
                else if (bpp == 24)
                {
                    b = d[xr + x * 3]; g = d[xr + x * 3 + 1]; r = d[xr + x * 3 + 2];
                }
                else
                {
                    int idx = bpp == 8 ? d[xr + x] : bpp == 4 ? (d[xr + x / 2] >> (x % 2 == 0 ? 4 : 0)) & 0xF : (d[xr + x / 8] >> (7 - x % 8)) & 1;
                    int pe = palOff + Math.Min(idx, palCount - 1) * 4;
                    b = d[pe]; g = d[pe + 1]; r = d[pe + 2];
                }
                bool masked = hasAnd && ((d[ar + x / 8] >> (7 - x % 8)) & 1) == 1;
                int rgb = (r << 16) | (g << 8) | b;
                img.Pixels[y * w + x] = bpp == 32 ? (a << 24) | rgb
                    : masked ? (rgb == 0 ? 0 : unchecked((int)0xFFE4E4E4))
                    : unchecked((int)0xFF000000) | rgb;
            }
        }
        if (bpp == 32 && !anyAlpha)
        {
            for (int y = 0; y < h; y++)
            {
                int ar = andOff + (bottomUp ? h - 1 - y : y) * andStride;
                for (int x = 0; x < w; x++)
                {
                    bool masked = hasAnd && ((d[ar + x / 8] >> (7 - x % 8)) & 1) == 1;
                    int rgb = img.Pixels[y * w + x] & 0xFFFFFF;
                    img.Pixels[y * w + x] = masked ? (rgb == 0 ? 0 : unchecked((int)0xFFE4E4E4)) : unchecked((int)0xFF000000) | rgb;
                }
            }
        }
        return img;
    }

    private static string Ascii(byte[] d, int off) => off + 4 <= d.Length ? Encoding.ASCII.GetString(d, off, 4) : "";
    private static int U16(byte[] d, int off) => BitConverter.ToUInt16(d, off);
    private static int I32(byte[] d, int off) => BitConverter.ToInt32(d, off);
    private static int BE32(byte[] d, int off) => (d[off] << 24) | (d[off + 1] << 16) | (d[off + 2] << 8) | d[off + 3];

    private static int[] Ints(byte[] d, int off, int count)
    {
        var r = new int[count];
        for (int i = 0; i < count; i++) r[i] = I32(d, off + i * 4);
        return r;
    }
}
