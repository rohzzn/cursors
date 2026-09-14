<#
  Generates every logo asset from one set of vector shapes:
    Cursors.ico              app icon, 16-256 px (small sizes as 32-bit DIBs, 256 px as PNG)
    docs\logo.svg            vector master
    docs\logo.png            512 px, used by the README
    docs\social-preview.png  1280x640 card for the GitHub repository settings

  Usage: powershell -ExecutionPolicy Bypass -File tools\make-logo.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$docs = Join-Path $root 'docs'
New-Item -ItemType Directory -Force $docs | Out-Null

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public static class CursorsLogo
{
    // Everything is laid out on a 512 x 512 canvas.
    const float Canvas = 512f, PlateInset = 20f, PlateRadius = 106f, PlateEdge = 3f;
    // Classic pointer outline in design units, tip at the origin.
    static readonly float[] Pointer = { 0f, 0f, 0f, 16.8f, 4.0f, 13.1f, 6.7f, 19.4f, 9.5f, 18.2f, 6.9f, 12.0f, 12.1f, 12.0f };
    const float Unit = 13.2f, OriginX = 158f, OriginY = 109f, EchoDx = 50f, EchoDy = 38f;
    const float FrontJoin = 6f, Gap = 16f;

    static readonly Color PlateTop = Color.FromArgb(0x30, 0x30, 0x30);
    static readonly Color PlateBottom = Color.FromArgb(0x1B, 0x1B, 0x1B);
    static readonly Color EchoColor = Color.FromArgb(0x7A, 0x7A, 0x7A);
    static readonly Color FrontColor = Color.FromArgb(0xF4, 0xF4, 0xF4);

    static PointF[] PointerAt(float x, float y)
    {
        var points = new PointF[Pointer.Length / 2];
        for (int i = 0; i < points.Length; i++) points[i] = new PointF(x + Pointer[i * 2] * Unit, y + Pointer[i * 2 + 1] * Unit);
        return points;
    }

    static GraphicsPath Polygon(PointF[] points)
    {
        var path = new GraphicsPath();
        path.AddPolygon(points);
        return path;
    }

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    static Brush PlateBrush()
    {
        return new LinearGradientBrush(new PointF(0, PlateInset - 1), new PointF(0, Canvas - PlateInset + 1), PlateTop, PlateBottom);
    }

    /// <summary>Draws the mark into a square of the given size at (x, y).</summary>
    public static void DrawMark(Graphics g, float x, float y, float size)
    {
        bool small = size <= 32;
        var outer = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TranslateTransform(x, y);
        g.ScaleTransform(size / Canvas, size / Canvas);

        var plate = new RectangleF(PlateInset, PlateInset, Canvas - 2 * PlateInset, Canvas - 2 * PlateInset);
        using (var path = RoundedRect(plate, PlateRadius))
        using (var brush = PlateBrush())
        {
            g.FillPath(brush, path);
            if (!small)
                using (var pen = new Pen(Color.FromArgb(18, 255, 255, 255), PlateEdge)) g.DrawPath(pen, path);
        }

        using (var echo = Polygon(PointerAt(OriginX + EchoDx, OriginY + EchoDy)))
        using (var front = Polygon(PointerAt(OriginX, OriginY)))
        {
            using (var brush = new SolidBrush(EchoColor)) g.FillPath(brush, echo);

            // Cut a plate-colored gap into the echo around the front pointer so the two read as separate cursors.
            var clip = g.Save();
            g.SetClip(echo, CombineMode.Intersect);
            using (var plateBrush = PlateBrush())
            using (var gap = new Pen(plateBrush, Gap) { LineJoin = LineJoin.Round })
                g.DrawPath(gap, front);
            g.Restore(clip);

            using (var brush = new SolidBrush(FrontColor)) g.FillPath(brush, front);
            using (var pen = new Pen(FrontColor, FrontJoin) { LineJoin = LineJoin.Round }) g.DrawPath(pen, front);
        }
        g.Restore(outer);
    }

    public static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) DrawMark(g, 0, 0, size);
        return bmp;
    }

    public static void SavePng(int size, string path)
    {
        using (var bmp = Render(size)) bmp.Save(path, ImageFormat.Png);
    }

    public static void WriteIcon(string path)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
        var images = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
            using (var bmp = Render(sizes[i])) images[i] = sizes[i] >= 256 ? Png(bmp) : Dib(bmp);

        using (var fs = File.Create(path))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
                w.Write((ushort)1); w.Write((ushort)32); w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
        }
    }

    static byte[] Png(Bitmap bmp)
    {
        using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    static byte[] Dib(Bitmap bmp)
    {
        int s = bmp.Width, maskStride = ((s + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(40); w.Write(s); w.Write(s * 2); w.Write((ushort)1); w.Write((ushort)32);
            w.Write(0); w.Write(s * s * 4 + maskStride * s); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            var data = bmp.LockBits(new Rectangle(0, 0, s, s), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var row = new byte[s * 4];
            for (int y = s - 1; y >= 0; y--)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                w.Write(row);
            }
            bmp.UnlockBits(data);
            w.Write(new byte[maskStride * s]); // all-zero AND mask: alpha does the work
            return ms.ToArray();
        }
    }

    static string Path(float x, float y)
    {
        var sb = new StringBuilder();
        var points = PointerAt(x, y);
        for (int i = 0; i < points.Length; i++)
            sb.Append(i == 0 ? "M" : "L").Append(F(points[i].X)).Append(' ').Append(F(points[i].Y)).Append(' ');
        return sb.Append('Z').ToString();
    }

    static string F(float v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }

    public static string Svg()
    {
        string echo = Path(OriginX + EchoDx, OriginY + EchoDy), front = Path(OriginX, OriginY);
        float plate = Canvas - 2 * PlateInset;
        var sb = new StringBuilder();
        sb.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"512\" height=\"512\" viewBox=\"0 0 512 512\">");
        sb.AppendLine("  <title>Cursors</title>");
        sb.AppendLine("  <defs>");
        sb.AppendLine("    <linearGradient id=\"plate\" gradientUnits=\"userSpaceOnUse\" x1=\"0\" y1=\"19\" x2=\"0\" y2=\"493\">");
        sb.AppendLine("      <stop offset=\"0\" stop-color=\"#303030\"/>");
        sb.AppendLine("      <stop offset=\"1\" stop-color=\"#1b1b1b\"/>");
        sb.AppendLine("    </linearGradient>");
        sb.AppendLine("    <clipPath id=\"echo\"><path d=\"" + echo + "\"/></clipPath>");
        sb.AppendLine("  </defs>");
        sb.AppendLine("  <rect x=\"" + F(PlateInset) + "\" y=\"" + F(PlateInset) + "\" width=\"" + F(plate) + "\" height=\"" + F(plate) + "\" rx=\"" + F(PlateRadius) + "\" fill=\"url(#plate)\" stroke=\"#ffffff\" stroke-opacity=\"0.07\" stroke-width=\"" + F(PlateEdge) + "\"/>");
        sb.AppendLine("  <path d=\"" + echo + "\" fill=\"#7a7a7a\"/>");
        sb.AppendLine("  <path d=\"" + front + "\" fill=\"none\" stroke=\"url(#plate)\" stroke-width=\"" + F(Gap) + "\" stroke-linejoin=\"round\" clip-path=\"url(#echo)\"/>");
        sb.AppendLine("  <path d=\"" + front + "\" fill=\"#f4f4f4\" stroke=\"#f4f4f4\" stroke-width=\"" + F(FrontJoin) + "\" stroke-linejoin=\"round\"/>");
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    static Font CreateFont(float px, params string[] families)
    {
        foreach (string name in families)
        {
            try
            {
                using (var family = new FontFamily(name))
                    if (family.IsStyleAvailable(FontStyle.Regular)) return new Font(family, px, FontStyle.Regular, GraphicsUnit.Pixel);
            }
            catch (ArgumentException) { }
        }
        return new Font(FontFamily.GenericSansSerif, px, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    public static void SaveSocialPreview(string path)
    {
        using (var bmp = new Bitmap(1280, 640, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(0x16, 0x16, 0x16));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var glow = new GraphicsPath())
            {
                glow.AddEllipse(30, 60, 620, 520);
                using (var brush = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(26, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } })
                    g.FillPath(brush, glow);
            }
            DrawMark(g, 170, 180, 280);

            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var title = CreateFont(124, "Segoe UI Variable Display Semib", "Segoe UI Semibold"))
            using (var tagline = CreateFont(42, "Segoe UI Variable Display", "Segoe UI"))
            using (var detail = CreateFont(30, "Segoe UI Variable Text", "Segoe UI"))
            using (var white = new SolidBrush(Color.FromArgb(0xF2, 0xF2, 0xF2)))
            using (var gray = new SolidBrush(Color.FromArgb(0xA0, 0xA0, 0xA0)))
            using (var dim = new SolidBrush(Color.FromArgb(0x70, 0x70, 0x70)))
            {
                g.DrawString("Cursors", title, white, 500, 178);
                g.DrawString("One-click cursor packs for Windows", tagline, gray, 510, 352);
                // Character code, not a literal: Windows PowerShell 5.1 reads BOM-less scripts as ANSI.
                g.DrawString("60 openly licensed packs  " + (char)0xB7 + "  Free and open source", detail, dim, 512, 420);
            }
            bmp.Save(path, ImageFormat.Png);
        }
    }
}
'@

[CursorsLogo]::WriteIcon((Join-Path $root 'Cursors.ico'))
[IO.File]::WriteAllText((Join-Path $docs 'logo.svg'), [CursorsLogo]::Svg(), (New-Object System.Text.UTF8Encoding $false))
[CursorsLogo]::SavePng(512, (Join-Path $docs 'logo.png'))
[CursorsLogo]::SaveSocialPreview((Join-Path $docs 'social-preview.png'))
Write-Host "Wrote Cursors.ico, docs\logo.svg, docs\logo.png, docs\social-preview.png"
