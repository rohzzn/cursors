using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Cursors.PackTool;

/// <summary>One cursor image: straight (non-premultiplied) ARGB pixels plus hotspot.</summary>
internal sealed class CursorImage
{
    public int Width, Height, HotX, HotY;
    public int[] Pixels;

    public CursorImage(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new int[width * height];
    }

    public static CursorImage FromBitmap(Bitmap source, int hotX, int hotY)
    {
        var img = new CursorImage(source.Width, source.Height) { HotX = hotX, HotY = hotY };
        using var argb = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(argb))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(source, 0, 0, source.Width, source.Height);
        }
        var bd = argb.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < img.Height; y++) Marshal.Copy(bd.Scan0 + y * bd.Stride, img.Pixels, y * img.Width, img.Width);
        }
        finally
        {
            argb.UnlockBits(bd);
        }
        return img;
    }

    public Bitmap ToBitmap()
    {
        var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < Height; y++) Marshal.Copy(Pixels, y * Width, bd.Scan0 + y * bd.Stride, Width);
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
        return bmp;
    }

    /// <summary>Resamples to a square canvas of <paramref name="size"/>, scaling the hotspot with it.</summary>
    public CursorImage Resize(int size, bool pixelArt)
    {
        if (Width == size && Height == size) return this;
        double sx = (double)size / Width, sy = (double)size / Height;
        using var src = ToBitmap();
        using var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        using (var attrs = new ImageAttributes())
        {
            attrs.SetWrapMode(WrapMode.TileFlipXY);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = pixelArt ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(src, new Rectangle(0, 0, size, size), 0, 0, Width, Height, GraphicsUnit.Pixel, attrs);
        }
        var result = FromBitmap(dst, (int)Math.Min(size - 1, Math.Floor(HotX * sx + (pixelArt ? 0 : 0.25))),
            (int)Math.Min(size - 1, Math.Floor(HotY * sy + (pixelArt ? 0 : 0.25))));
        return result;
    }

    public int OpaquePixelCount() => Pixels.Count(p => (uint)p >> 24 > 16);
}

/// <summary>A cursor as a sequence of frames; each frame holds the same picture at several sizes.</summary>
internal sealed class CursorAnimation
{
    public readonly List<List<CursorImage>> Frames = new(); // frame -> sizes
    public readonly List<int> DelaysMs = new();

    public bool IsAnimated => Frames.Count > 1;
}

/// <summary>Reader for X11 Xcursor files (the format Linux cursor themes ship in).</summary>
internal static class XCursorReader
{
    private const uint ImageType = 0xFFFD0002;

    public static bool IsXCursor(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var head = new byte[4];
            return fs.Read(head, 0, 4) == 4 && Encoding.ASCII.GetString(head) == "Xcur";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Returns nominal size -> frames (in file order) with delays.</summary>
    public static SortedDictionary<int, List<(CursorImage Image, int Delay)>> Read(string path)
    {
        byte[] d = File.ReadAllBytes(path);
        if (d.Length < 16 || Encoding.ASCII.GetString(d, 0, 4) != "Xcur") throw new InvalidDataException("not an Xcursor file: " + path);
        int headerSize = BitConverter.ToInt32(d, 4);
        int ntoc = BitConverter.ToInt32(d, 12);
        var bySize = new SortedDictionary<int, List<(CursorImage, int)>>();
        for (int i = 0; i < ntoc; i++)
        {
            int e = headerSize + i * 12;
            if (BitConverter.ToUInt32(d, e) != ImageType) continue;
            int nominal = BitConverter.ToInt32(d, e + 4);
            int pos = BitConverter.ToInt32(d, e + 8);
            int w = BitConverter.ToInt32(d, pos + 16), h = BitConverter.ToInt32(d, pos + 20);
            int xhot = BitConverter.ToInt32(d, pos + 24), yhot = BitConverter.ToInt32(d, pos + 28);
            int delay = BitConverter.ToInt32(d, pos + 32);
            int px = pos + BitConverter.ToInt32(d, pos);
            if (w <= 0 || h <= 0 || w > 1024 || h > 1024 || px + w * h * 4 > d.Length) continue;

            var img = new CursorImage(w, h) { HotX = Math.Min(xhot, w - 1), HotY = Math.Min(yhot, h - 1) };
            for (int p = 0; p < w * h; p++)
            {
                uint argb = BitConverter.ToUInt32(d, px + p * 4);
                uint a = argb >> 24;
                if (a == 0)
                {
                    img.Pixels[p] = 0;
                    continue;
                }
                // Xcursor pixels are premultiplied; ICO/CUR expects straight alpha.
                uint r = Math.Min(255, ((argb >> 16) & 0xFF) * 255 / a);
                uint g = Math.Min(255, ((argb >> 8) & 0xFF) * 255 / a);
                uint b = Math.Min(255, (argb & 0xFF) * 255 / a);
                img.Pixels[p] = unchecked((int)((a << 24) | (r << 16) | (g << 8) | b));
            }
            if (!bySize.TryGetValue(nominal, out var list)) bySize[nominal] = list = new List<(CursorImage, int)>();
            list.Add((img, delay));
        }
        return bySize;
    }

    /// <summary>Builds a multi-size animation, keeping only sizes whose frame count matches the most detailed size.</summary>
    public static CursorAnimation ToAnimation(SortedDictionary<int, List<(CursorImage Image, int Delay)>> bySize, IReadOnlyList<int> wantedSizes)
    {
        var sizes = PickSizes(bySize.Keys.ToList(), wantedSizes);
        int frameCount = sizes.Max(s => bySize[s].Count);
        sizes = sizes.Where(s => bySize[s].Count == frameCount).ToList();

        var anim = new CursorAnimation();
        var reference = bySize[sizes.Last()];
        for (int f = 0; f < frameCount; f++)
        {
            anim.Frames.Add(sizes.Select(s => bySize[s][f].Image).ToList());
            anim.DelaysMs.Add(reference[f].Delay);
        }
        return anim;
    }

    private static List<int> PickSizes(List<int> available, IReadOnlyList<int> wanted)
    {
        var chosen = new SortedSet<int>();
        foreach (int w in wanted)
        {
            int exact = available.FirstOrDefault(a => a == w);
            if (exact != 0) chosen.Add(exact);
        }
        if (chosen.Count == 0) chosen.Add(available.OrderBy(a => Math.Abs(a - 32)).First());
        // Always keep the largest available image up to the biggest wanted size, for sharp scaling.
        int largest = available.Where(a => a <= wanted.Max()).DefaultIfEmpty(available.Min()).Max();
        chosen.Add(largest);
        return chosen.ToList();
    }
}

/// <summary>Writes Windows .cur (ICO type 2) and .ani (RIFF ACON) files.</summary>
internal static class WindowsCursorWriter
{
    public static void Write(string path, CursorAnimation anim)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        if (anim.IsAnimated)
        {
            if (!path.EndsWith(".ani", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("animated cursor needs .ani: " + path);
            File.WriteAllBytes(path, BuildAni(anim));
        }
        else
        {
            if (!path.EndsWith(".cur", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("static cursor needs .cur: " + path);
            File.WriteAllBytes(path, BuildCur(anim.Frames[0]));
        }
    }

    public static byte[] BuildCur(IReadOnlyList<CursorImage> images)
    {
        var ordered = images.OrderByDescending(i => i.Width).ToList();
        var blobs = ordered.Select(EncodeImage).ToList();
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)0);
        bw.Write((ushort)2);
        bw.Write((ushort)ordered.Count);
        int offset = 6 + 16 * ordered.Count;
        for (int i = 0; i < ordered.Count; i++)
        {
            var img = ordered[i];
            bw.Write((byte)(img.Width >= 256 ? 0 : img.Width));
            bw.Write((byte)(img.Height >= 256 ? 0 : img.Height));
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((ushort)img.HotX);
            bw.Write((ushort)img.HotY);
            bw.Write(blobs[i].Length);
            bw.Write(offset);
            offset += blobs[i].Length;
        }
        foreach (var blob in blobs) bw.Write(blob);
        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>PNG-compressed entries, as the upstream Windows builds of Bibata and friends ship (Vista and later).</summary>
    public static byte[] EncodeImage(CursorImage img) => EncodePng(img);

    public static byte[] EncodePng(CursorImage img)
    {
        using var bmp = img.ToBitmap();
        using var png = new MemoryStream();
        bmp.Save(png, ImageFormat.Png);
        return png.ToArray();
    }

    public static byte[] EncodeBmp(CursorImage img)
    {

        int w = img.Width, h = img.Height;
        int maskStride = ((w + 31) / 32) * 4;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(40);
        bw.Write(w);
        bw.Write(h * 2);
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write(0);
        bw.Write(w * h * 4 + maskStride * h);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        for (int y = h - 1; y >= 0; y--)
            for (int x = 0; x < w; x++)
            {
                int p = img.Pixels[y * w + x];
                bool clear = (uint)p >> 24 == 0;
                bw.Write(clear ? 0 : p); // BGRA little-endian == ARGB int
            }
        var mask = new byte[maskStride];
        for (int y = h - 1; y >= 0; y--)
        {
            Array.Clear(mask, 0, mask.Length);
            for (int x = 0; x < w; x++)
                if ((uint)img.Pixels[y * w + x] >> 24 == 0) mask[x / 8] |= (byte)(0x80 >> (x % 8));
            bw.Write(mask);
        }
        bw.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildAni(CursorAnimation anim)
    {
        var frames = anim.Frames.Select(BuildCur).ToList();
        var jiffies = anim.DelaysMs.Select(ms => Math.Max(1, (int)Math.Round(ms * 60.0 / 1000))).ToList();
        bool uniform = jiffies.All(j => j == jiffies[0]);

        using var body = new MemoryStream();
        using var bw = new BinaryWriter(body);
        bw.Write(Encoding.ASCII.GetBytes("ACON"));

        bw.Write(Encoding.ASCII.GetBytes("anih"));
        bw.Write(36);
        bw.Write(36);
        bw.Write(frames.Count);
        bw.Write(frames.Count);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(jiffies[0]);
        bw.Write(1); // AF_ICON

        if (!uniform)
        {
            bw.Write(Encoding.ASCII.GetBytes("rate"));
            bw.Write(jiffies.Count * 4);
            foreach (int j in jiffies) bw.Write(j);
        }

        int listSize = 4 + frames.Sum(f => 8 + f.Length + (f.Length & 1));
        bw.Write(Encoding.ASCII.GetBytes("LIST"));
        bw.Write(listSize);
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
}
