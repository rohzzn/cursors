using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Cursors.PackTool;

internal sealed class BuiltPack
{
    public string Id, Dir, Name, Category, Author, License, Url, Mapped, Substituted;
    public readonly string[] Files = new string[CursorPack.RoleCount];

    public static List<BuiltPack> LoadAll(string packsDir) =>
        Directory.GetDirectories(packsDir)
            .Where(d => File.Exists(Path.Combine(d, "pack.ini")))
            .Select(Load)
            .OrderBy(p => p.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static BuiltPack Load(string dir)
    {
        var v = IniFile.Read(Path.Combine(dir, "pack.ini"));
        string Get(string k) => v.TryGetValue(k, out var s) ? s : "";
        var pack = new BuiltPack
        {
            Id = Path.GetFileName(dir), Dir = dir, Name = Get("name"), Category = Get("category"), Author = Get("author"),
            License = Get("license"), Url = Get("url"), Mapped = Get("mapped"), Substituted = Get("substituted"),
        };
        for (int i = 0; i < CursorPack.RoleCount; i++)
        {
            string f = Get(CursorPack.RegistryNames[i]);
            pack.Files[i] = f.Length > 0 ? f : null;
        }
        return pack;
    }

    public string PathFor(int role) => Files[role] == null ? null : Path.Combine(Dir, Files[role]);

    public bool IsSubstituted(int role) =>
        Substituted.Split(',').Any(s => s.Trim().StartsWith(CursorPack.RegistryNames[role] + ">", StringComparison.Ordinal));
}

internal static class PackVerifier
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot, yHotspot;
        public IntPtr hbmMask, hbmColor;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadCursorFromFile(string path);
    [DllImport("user32.dll")] private static extern bool DestroyCursor(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance, IntPtr id);
    [DllImport("user32.dll")] private static extern IntPtr CopyIcon(IntPtr h);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr h, out ICONINFO info);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, IntPtr pv, uint winIni);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);

    private static readonly int[] SystemCursorIds =
        { 32512, 32651, 32650, 32514, 32515, 32513, 32631, 32648, 32645, 32644, 32642, 32643, 32646, 32516, 32649, 32671, 32672 };

    private static readonly string[] ShortRoles =
        { "Normal", "Help", "Working", "Busy", "Precision", "Text", "Pen", "No", "Vert", "Horz", "Diag1", "Diag2", "Move", "Alt", "Link", "Pin", "Person" };

    public static int Verify(string packsDir, string reportDir)
    {
        Directory.CreateDirectory(reportDir);
        var packs = BuiltPack.LoadAll(packsDir);
        var report = new StringBuilder();
        report.AppendLine("| Pack | Category | Native roles | Animated | Sizes | License | Result |");
        report.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        int problems = 0;
        var fingerprints = new List<(BuiltPack Pack, float[] Print)>();

        foreach (var pack in packs)
        {
            var issues = new List<string>();
            var animated = new List<string>();
            var sizes = new SortedSet<int>();
            foreach (var (key, value) in new[] { ("name", pack.Name), ("category", pack.Category), ("author", pack.Author), ("license", pack.License), ("url", pack.Url) })
                if (string.IsNullOrWhiteSpace(value)) issues.Add("missing " + key);

            int native = 0;
            for (int i = 0; i < CursorPack.RoleCount; i++)
            {
                string path = pack.PathFor(i);
                if (path == null)
                {
                    issues.Add(CursorPack.RegistryNames[i] + ": no file");
                    continue;
                }
                if (!pack.IsSubstituted(i)) native++;
                if (!File.Exists(path))
                {
                    issues.Add(CursorPack.RegistryNames[i] + ": file missing");
                    continue;
                }
                try
                {
                    byte[] data = File.ReadAllBytes(path);
                    var frames = WindowsCursorFiles.ReadFrames(data);
                    bool isAni = WindowsCursorFiles.IsAni(data);
                    if (isAni != path.EndsWith(".ani", StringComparison.OrdinalIgnoreCase)) issues.Add($"{pack.Files[i]}: extension does not match content");
                    if (isAni && !animated.Contains(ShortRoles[i])) animated.Add(ShortRoles[i]);
                    int maxOpaque = 0;
                    foreach (var entry in frames.SelectMany(f => f))
                    {
                        sizes.Add(entry.Width);
                        if (entry.HotX >= entry.Width || entry.HotY >= entry.Height) issues.Add($"{pack.Files[i]}: hotspot outside {entry.Width}px image");
                        var img = WindowsCursorFiles.Decode(entry);
                        maxOpaque = Math.Max(maxOpaque, img.OpaquePixelCount());
                    }
                    if (maxOpaque < 8) issues.Add($"{pack.Files[i]}: image is blank");
                    using (var preview = CursorDecoder.CreatePreview(path, 48))
                        if (preview == null) issues.Add($"{pack.Files[i]}: app preview decoder rejected it");
                    IntPtr handle = LoadCursorFromFile(path);
                    if (handle == IntPtr.Zero) issues.Add($"{pack.Files[i]}: Windows LoadCursorFromFile failed ({Marshal.GetLastWin32Error()})");
                    else DestroyCursor(handle);
                }
                catch (Exception ex)
                {
                    issues.Add($"{pack.Files[i]}: {ex.Message}");
                }
            }
            if (pack.Files[CursorPack.Arrow] != null && File.Exists(pack.PathFor(CursorPack.Arrow)))
                fingerprints.Add((pack, Fingerprint(pack.PathFor(CursorPack.Arrow))));

            problems += issues.Count;
            string result = issues.Count == 0 ? "OK" : "**" + string.Join("; ", issues.Distinct()) + "**";
            report.AppendLine($"| {pack.Name} (`{pack.Id}`) | {pack.Category} | {native}/17 | {(animated.Count == 0 ? "-" : string.Join(", ", animated))} | {string.Join("/", sizes)} | {pack.License} | {result} |");
            Console.WriteLine($"{(issues.Count == 0 ? "ok    " : "ISSUE ")} {pack.Id,-28} native {native,2}/17  {string.Join("; ", issues.Distinct())}");
        }

        report.AppendLine();
        report.AppendLine("Near-identical normal pointers (possible duplicates):");
        int dupes = 0;
        for (int a = 0; a < fingerprints.Count; a++)
            for (int b = a + 1; b < fingerprints.Count; b++)
            {
                double d = Distance(fingerprints[a].Print, fingerprints[b].Print);
                if (d >= 0.035) continue;
                dupes++;
                string line = $"- {fingerprints[a].Pack.Id} ~ {fingerprints[b].Pack.Id} (distance {d:F3})";
                report.AppendLine(line);
                Console.WriteLine("similar " + line.Substring(2));
            }
        if (dupes == 0) report.AppendLine("- none");

        File.WriteAllText(Path.Combine(reportDir, "report.md"), report.ToString());
        foreach (var group in packs.GroupBy(p => p.Category))
            DrawSheet(group.ToList(), Path.Combine(reportDir, "sheet-" + Safe(group.Key) + ".png"));
        Console.WriteLine($"{packs.Count} packs, {problems} problem(s), {dupes} similar pair(s)");
        return problems;
    }

    private static string Safe(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-').ToArray());

    private static float[] Fingerprint(string path)
    {
        const int n = 24;
        var print = new float[n * n * 2];
        using var preview = CursorDecoder.CreatePreview(path, n);
        if (preview == null) return print;
        var frame = preview.Frames[preview.Sequence[0]];
        using var canvas = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas)) g.DrawImage(frame, (n - frame.Width) / 2, (n - frame.Height) / 2, frame.Width, frame.Height);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var c = canvas.GetPixel(x, y);
                print[(y * n + x) * 2] = c.A / 255f;
                print[(y * n + x) * 2 + 1] = c.A / 255f * (c.R * 0.3f + c.G * 0.59f + c.B * 0.11f) / 255f;
            }
        return print;
    }

    private static double Distance(float[] a, float[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]);
        return sum / a.Length;
    }

    /// <summary>One row per pack, one cell per role, with the hotspot marked in red.</summary>
    private static void DrawSheet(List<BuiltPack> packs, string path)
    {
        const int labelW = 230, cell = 58, headerH = 26, rowH = 64, box = 44;
        int width = labelW + cell * CursorPack.RoleCount, height = headerH + rowH * packs.Count;
        using var sheet = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(28, 28, 28));
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var font = new Font("Segoe UI", 8.5f);
        using var bold = new Font("Segoe UI Semibold", 9f);
        using var dim = new SolidBrush(Color.FromArgb(150, 150, 150));
        using var light = new SolidBrush(Color.FromArgb(235, 235, 235));
        using var checkA = new SolidBrush(Color.FromArgb(52, 52, 52));
        using var checkB = new SolidBrush(Color.FromArgb(70, 70, 70));
        using var subst = new SolidBrush(Color.FromArgb(255, 150, 40));
        using var aniDot = new SolidBrush(Color.FromArgb(60, 200, 255));
        using var hotPen = new Pen(Color.FromArgb(255, 40, 40), 1.5f);

        for (int i = 0; i < CursorPack.RoleCount; i++)
            g.DrawString(ShortRoles[i], font, dim, labelW + cell * i + 4, 6);

        for (int row = 0; row < packs.Count; row++)
        {
            var pack = packs[row];
            int top = headerH + row * rowH;
            g.DrawString(pack.Name, bold, light, 8, top + 12);
            g.DrawString(pack.Id + " · " + pack.License, font, dim, 8, top + 32);
            for (int i = 0; i < CursorPack.RoleCount; i++)
            {
                int left = labelW + cell * i;
                for (int cy = 0; cy < 6; cy++)
                    for (int cx = 0; cx < 6; cx++)
                        g.FillRectangle((cx + cy) % 2 == 0 ? checkA : checkB, left + 2 + cx * 9, top + 5 + cy * 9, 9, 9);
                string file = pack.PathFor(i);
                if (file == null || !File.Exists(file)) continue;
                try
                {
                    byte[] data = File.ReadAllBytes(file);
                    var entries = WindowsCursorFiles.ReadFrames(data)[0];
                    var entry = entries.OrderBy(e => e.Width < 48 ? 1000 - e.Width : e.Width).First();
                    var img = WindowsCursorFiles.Decode(entry);
                    float scale = (float)box / Math.Max(img.Width, img.Height);
                    using var bmp = img.ToBitmap();
                    float ox = left + 2 + (54 - img.Width * scale) / 2, oy = top + 5 + (54 - img.Height * scale) / 2;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(bmp, ox, oy, img.Width * scale, img.Height * scale);
                    float hx = ox + (img.HotX + 0.5f) * scale, hy = oy + (img.HotY + 0.5f) * scale;
                    g.DrawLine(hotPen, hx - 3, hy, hx + 3, hy);
                    g.DrawLine(hotPen, hx, hy - 3, hx, hy + 3);
                    if (WindowsCursorFiles.IsAni(data)) g.FillEllipse(aniDot, left + 47, top + 50, 7, 7);
                    if (pack.IsSubstituted(i)) g.FillRectangle(subst, left + 2, top + 5, 7, 7);
                }
                catch
                {
                    g.DrawString("ERR", bold, subst, left + 12, top + 24);
                }
            }
        }
        sheet.Save(path, ImageFormat.Png);
    }

    /// <summary>
    /// Applies every pack for real and checks that Windows loaded each role: a file Windows rejects falls back to the
    /// built-in cursor, so its system cursor bitmap would equal the all-empty baseline. Restores the user's settings.
    /// </summary>
    public static int ApplyTest(string packsDir, string[] only)
    {
        var packs = BuiltPack.LoadAll(packsDir).Where(p => only.Length == 0 || only.Contains(p.Id)).ToList();
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors", writable: true);
        var backup = key.GetValueNames().ToDictionary(n => n, n => (key.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames), key.GetValueKind(n)));
        int failures = 0;
        try
        {
            foreach (string role in CursorPack.RegistryNames) key.SetValue(role, "", RegistryValueKind.String);
            Reload();
            var baseline = SystemCursorIds.Select(HashSystemCursor).ToArray();

            foreach (var pack in packs)
            {
                for (int i = 0; i < CursorPack.RoleCount; i++)
                    key.SetValue(CursorPack.RegistryNames[i], pack.PathFor(i) == null ? "" : Path.GetFullPath(pack.PathFor(i)), RegistryValueKind.String);
                Reload();
                var failed = Enumerable.Range(0, CursorPack.RoleCount)
                    .Where(i => pack.PathFor(i) != null && HashSystemCursor(SystemCursorIds[i]) == baseline[i])
                    .Select(i => CursorPack.RegistryNames[i]).ToList();
                failures += failed.Count;
                Console.WriteLine($"{(failed.Count == 0 ? "applied" : "FAILED ")} {pack.Id,-28} {(failed.Count == 0 ? "all 17 roles loaded by Windows" : "not loaded: " + string.Join(", ", failed))}");
            }
        }
        finally
        {
            foreach (string name in key.GetValueNames())
                if (!backup.ContainsKey(name)) key.DeleteValue(name, false);
            foreach (var kv in backup) key.SetValue(kv.Key, kv.Value.Item1, kv.Value.Item2);
            Reload();
            Console.WriteLine("restored original cursor settings");
        }
        return failures;
    }

    private static void Reload() => SystemParametersInfo(0x57, 0, IntPtr.Zero, 0x03);

    private static string HashSystemCursor(int id)
    {
        IntPtr copy = CopyIcon(LoadCursor(IntPtr.Zero, (IntPtr)id));
        if (copy == IntPtr.Zero) return "none";
        try
        {
            if (!GetIconInfo(copy, out var info)) return "noinfo";
            using var ms = new MemoryStream();
            foreach (IntPtr hbm in new[] { info.hbmColor, info.hbmMask })
            {
                if (hbm == IntPtr.Zero) continue;
                using (var bmp = Image.FromHbitmap(hbm)) bmp.Save(ms, ImageFormat.Bmp);
                DeleteObject(hbm);
            }
            using var sha = SHA1.Create();
            return BitConverter.ToString(sha.ComputeHash(ms.ToArray())) + $"@{info.xHotspot},{info.yHotspot}";
        }
        finally
        {
            DestroyIcon(copy);
        }
    }
}
