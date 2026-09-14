using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Cursors.PackTool;

/// <summary>One [section] of tools/packs.recipe.</summary>
internal sealed class Recipe
{
    public string Id;
    public readonly Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase);

    public string Get(string key) => Values.TryGetValue(key, out var v) && v.Trim().Length > 0 ? v.Trim() : null;

    public static List<Recipe> Load(string path)
    {
        var all = new List<Recipe>();
        Recipe current = null;
        int lineNo = 0;
        foreach (string raw in File.ReadAllLines(path))
        {
            lineNo++;
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
            if (line[0] == '[' && line[line.Length - 1] == ']')
            {
                current = new Recipe { Id = line.Substring(1, line.Length - 2).Trim() };
                if (all.Any(r => r.Id.Equals(current.Id, StringComparison.OrdinalIgnoreCase)))
                    throw new FormatException($"{path}:{lineNo}: duplicate section [{current.Id}]");
                all.Add(current);
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0 || current == null) throw new FormatException($"{path}:{lineNo}: expected key = value");
            string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
            if (key.Equals("include", StringComparison.OrdinalIgnoreCase))
            {
                var template = all.FirstOrDefault(r => r.Id.Equals(value, StringComparison.OrdinalIgnoreCase))
                    ?? throw new FormatException($"{path}:{lineNo}: unknown template [{value}]");
                foreach (var kv in template.Values) current.Values[kv.Key] = kv.Value;
                continue;
            }
            current.Values[key] = value;
        }
        return all;
    }
}

internal static class PackBuilder
{
    private static readonly string[] Roles = CursorPack.RegistryNames;
    private const int RoleCount = CursorPack.RoleCount;

    // When a pack has no image for a role, reuse its closest sibling instead of a mismatched Windows cursor.
    private static readonly int[][] Substitutes =
    {
        new int[0],                                               // Arrow
        new[] { CursorPack.Arrow },                               // Help
        new[] { CursorPack.Wait, CursorPack.Arrow },              // AppStarting
        new[] { CursorPack.AppStarting, CursorPack.Arrow },       // Wait
        new[] { CursorPack.Arrow },                               // Crosshair
        new[] { CursorPack.Arrow },                               // IBeam
        new[] { CursorPack.Arrow },                               // NWPen
        new[] { CursorPack.Arrow },                               // No
        new[] { CursorPack.Arrow },                               // SizeNS
        new[] { CursorPack.Arrow },                               // SizeWE
        new[] { CursorPack.Arrow },                               // SizeNWSE
        new[] { CursorPack.Arrow },                               // SizeNESW
        new[] { CursorPack.Arrow },                               // SizeAll
        new[] { CursorPack.Arrow },                               // UpArrow
        new[] { CursorPack.Arrow },                               // Hand
        new[] { CursorPack.Hand, CursorPack.Arrow },              // Pin
        new[] { CursorPack.Hand, CursorPack.Arrow },              // Person
    };

    // X11 cursor names per Windows role, most specific first.
    internal static readonly string[][] X11Names =
    {
        new[] { "default", "left_ptr", "arrow", "top_left_arrow" },
        new[] { "help", "question_arrow", "left_ptr_help", "whats_this", "5c6cd98b3f3ebcb1f9c7f1c204630408", "d9ce0ab605698f320427677b458ad60b" },
        new[] { "progress", "left_ptr_watch", "half-busy", "00000000000000020006000e7e9ffc3f", "3ecb610c1bf2410f44200f48c40d3599", "08e8e1c95fe2fc01f976f1e063a24ccd" },
        new[] { "wait", "watch" },
        new[] { "crosshair", "cross", "tcross" },
        new[] { "text", "xterm", "ibeam" },
        new[] { "pencil", "draft" },
        new[] { "not-allowed", "forbidden", "crossed_circle", "circle", "no-drop", "03b6e0fcb3499374a867c041f52298f0" },
        new[] { "ns-resize", "size_ver", "v_double_arrow", "sb_v_double_arrow", "00008160000006810000408080010102" },
        new[] { "ew-resize", "size_hor", "h_double_arrow", "sb_h_double_arrow", "028006030e0e7ebffc7f7070c0600140" },
        new[] { "nwse-resize", "size_fdiag", "bd_double_arrow", "c7088f0f3e6c8088236ef8e1e3e70000" },
        new[] { "nesw-resize", "size_bdiag", "fd_double_arrow", "fcf1c3c7cd4491d801f1e1c78f100000" },
        new[] { "move", "fleur", "all-scroll", "size_all", "4498f0e0c1937ffe01fd06f973665830", "9081237383d90e509aa00f00170e968f" },
        new[] { "up-arrow", "up_arrow", "center_ptr", "sb_up_arrow" },
        new[] { "pointer", "hand2", "pointing_hand", "hand1", "e29285e634086352946a0e7090d73106", "9d800788f1b08800ae810202380a0822" },
        new string[0],
        new string[0],
    };

    private sealed class Context
    {
        public Recipe Recipe;
        public string Root, OutRoot, OutDir, SourceDir, BaseDir;
        public readonly string[] Produced = new string[RoleCount];
        public readonly List<string> Mapped = new();
        public List<int> Keep, AniKeep;
    }

    public static int BuildAll(string recipePath, string root, string outRoot, string[] only)
    {
        int failures = 0, order = 0;
        foreach (var recipe in Recipe.Load(recipePath))
        {
            if (recipe.Id.StartsWith("_")) continue;
            order++;
            if (only.Length > 0 && !only.Contains(recipe.Id, StringComparer.OrdinalIgnoreCase)) continue;
            try
            {
                recipe.Values["order"] = order.ToString(CultureInfo.InvariantCulture);
                Build(recipe, root, outRoot);
                Console.WriteLine("built   " + recipe.Id);
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"FAILED  {recipe.Id}: {ex.Message}");
            }
        }
        return failures;
    }

    private static void Build(Recipe r, string root, string outRoot)
    {
        var c = new Context
        {
            Recipe = r,
            Root = root,
            OutRoot = outRoot,
            OutDir = Path.Combine(outRoot, r.Id),
            Keep = ParseInts(r.Get("keep") ?? "32,48,64,96,128"),
            // Animations dominate pack size; 32/48/64 cover 100-200% scaling and Windows scales beyond that.
            AniKeep = ParseInts(r.Get("anikeep") ?? "32,48,64"),
        };
        Directory.CreateDirectory(c.OutDir);
        foreach (string f in Directory.GetFiles(c.OutDir)) File.Delete(f);

        string from = r.Get("from") ?? throw new FormatException("missing 'from'");
        int space = from.IndexOf(' ');
        string kind = space < 0 ? from : from.Substring(0, space);
        string arg = space < 0 ? "" : from.Substring(space + 1).Trim();
        // \\?\ paths are passed to Windows verbatim, so recipe paths must use backslashes.
        c.SourceDir = c.BaseDir = arg.Length > 0 ? Path.Combine(root, arg.Replace('/', '\\')) : root;

        switch (kind)
        {
            case "windows":
                ImportWindows(c);
                break;
            case "xcursor":
                for (int i = 0; i < RoleCount; i++)
                    foreach (string name in X11Names[i])
                    {
                        string path = ResolveX11(c.SourceDir, name);
                        if (path == null) continue;
                        c.Produced[i] = ConvertX11(c, path, Roles[i]);
                        break;
                    }
                break;
            case "png":
                break;
            case "derive":
                Derive(c, Path.Combine(outRoot, arg));
                break;
            default:
                throw new FormatException("unknown source kind: " + kind);
        }

        for (int i = 0; i < RoleCount; i++)
        {
            string spec = r.Get(Roles[i]);
            if (spec != null && !spec.StartsWith("@")) ApplySpec(c, i, spec);
        }
        for (int i = 0; i < RoleCount; i++)
        {
            string spec = r.Get(Roles[i]);
            if (spec == null || !spec.StartsWith("@")) continue;
            int other = RoleIndex(spec.Substring(1));
            c.Produced[i] = c.Produced[other] ?? throw new InvalidDataException($"{Roles[i]} refers to missing {Roles[other]}");
            c.Mapped.Add(Roles[i] + ">" + Roles[other]);
        }

        var substituted = new List<string>();
        for (int i = 0; i < RoleCount; i++)
        {
            if (c.Produced[i] != null) continue;
            foreach (int s in Substitutes[i])
            {
                if (c.Produced[s] == null) continue;
                c.Produced[i] = c.Produced[s];
                substituted.Add(Roles[i] + ">" + Roles[s]);
                break;
            }
        }
        if (c.Produced[CursorPack.Arrow] == null) throw new InvalidDataException("no normal pointer found");

        var ini = new List<KeyValuePair<string, string>>();
        foreach (string key in new[] { "name", "category", "order", "author", "license", "url", "notes" })
            ini.Add(new KeyValuePair<string, string>(key, r.Get(key) ?? ""));
        ini.Add(new KeyValuePair<string, string>("mapped", string.Join(", ", c.Mapped)));
        ini.Add(new KeyValuePair<string, string>("substituted", string.Join(", ", substituted)));
        for (int i = 0; i < RoleCount; i++) ini.Add(new KeyValuePair<string, string>(Roles[i], c.Produced[i]));
        IniFile.Write(Path.Combine(c.OutDir, "pack.ini"), ini);
    }

    // ---- Windows builds ---------------------------------------------------------------------------

    private static void ImportWindows(Context c)
    {
        string hint = c.Recipe.Get("variant");
        DetectedPack detected;
        if (c.Recipe.Get("detect") == "names")
        {
            var candidates = Directory.GetDirectories(c.SourceDir, "*", SearchOption.AllDirectories).Concat(new[] { c.SourceDir }).ToList();
            string dir = hint == null ? c.SourceDir
                : candidates.FirstOrDefault(d => Path.GetFileName(d).Equals(hint, StringComparison.OrdinalIgnoreCase))
                  ?? candidates.FirstOrDefault(d => Path.GetFileName(d).IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                  ?? throw new DirectoryNotFoundException("no folder matching " + hint);
            detected = PackDetection.FromFiles(Directory.GetFiles(dir).Where(PackDetection.IsCursorFile), c.Recipe.Id);
            c.BaseDir = dir;
        }
        else
        {
            var infs = Directory.GetFiles(c.SourceDir, "*.inf", SearchOption.AllDirectories)
                .Where(f => hint == null || f.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(f => f.Length).ToList();
            if (infs.Count == 0) throw new FileNotFoundException($"no install.inf under {c.SourceDir} matching '{hint}'");
            c.BaseDir = Path.GetDirectoryName(infs[0]);
            detected = PackDetection.ParseInf(infs[0], c.BaseDir);
        }
        for (int i = 0; i < RoleCount; i++)
            if (detected.Files[i] != null) c.Produced[i] = CopyCursor(c, detected.Files[i], Roles[i]);
    }

    private static string CopyCursor(Context c, string source, string role)
    {
        byte[] data = File.ReadAllBytes(source);
        string merge = c.Recipe.Get("merge"), hint = c.Recipe.Get("variant");
        if (merge != null && hint != null)
        {
            var siblings = merge.Split(',').Select(m => ReplaceFirst(source, hint, m.Trim())).Where(File.Exists).Select(File.ReadAllBytes);
            data = WindowsCursorFiles.MergeSizes(data, siblings);
        }
        bool ani = WindowsCursorFiles.IsAni(data);
        data = WindowsCursorFiles.TrimSizes(data, ani ? c.AniKeep : c.Keep);
        string name = role + (ani ? ".ani" : ".cur");
        File.WriteAllBytes(Path.Combine(c.OutDir, name), data);
        return name;
    }

    private static string ReplaceFirst(string text, string find, string replacement)
    {
        int i = text.IndexOf(find, StringComparison.OrdinalIgnoreCase);
        return i < 0 ? text : text.Substring(0, i) + replacement + text.Substring(i + find.Length);
    }

    // ---- X11 themes --------------------------------------------------------------------------------

    private static readonly Dictionary<string, Dictionary<string, string>> LinkCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Symlink tables written next to downloads (symlinks.tsv / _symlinks.tsv), keyed by "theme/cursors/name".</summary>
    private static Dictionary<string, string> LinksFor(string dir)
    {
        if (LinkCache.TryGetValue(dir, out var cached)) return cached;
        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
        {
            foreach (string name in new[] { "symlinks.tsv", "_symlinks.tsv" })
            {
                string tsv = Path.Combine(d.FullName, name);
                if (!File.Exists(tsv)) continue;
                foreach (string line in File.ReadAllLines(tsv))
                {
                    int tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    links[LastSegments(line.Substring(0, tab), 3)] = line.Substring(tab + 1).Trim();
                }
            }
            if (d.Name.Equals("raw", StringComparison.OrdinalIgnoreCase) || d.Name.Equals("work", StringComparison.OrdinalIgnoreCase)) break;
        }
        return LinkCache[dir] = links;
    }

    private static string LastSegments(string path, int count)
    {
        var parts = path.Replace('\\', '/').Trim('/').Split('/');
        return string.Join("/", parts.Skip(Math.Max(0, parts.Length - count))).ToLowerInvariant();
    }

    internal static string ResolveX11(string dir, string name)
    {
        var links = LinksFor(dir);
        string prefix = LastSegments(dir, 2);
        for (int hop = 0; hop < 10; hop++)
        {
            if (links.TryGetValue(prefix + "/" + name.ToLowerInvariant(), out var target))
            {
                name = Path.GetFileName(target.Replace('/', '\\').TrimEnd('\\'));
                continue;
            }
            string path = Path.Combine(dir, name);
            if (!File.Exists(path)) return null;
            if (XCursorReader.IsXCursor(path)) return path;
            long length = new FileInfo(path).Length;
            if (length > 0 && length < 256)
            {
                string text = File.ReadAllText(path).Trim();
                if (text.Length > 0 && text.IndexOfAny(Path.GetInvalidPathChars()) < 0)
                {
                    name = Path.GetFileName(text.Replace('/', '\\'));
                    continue;
                }
            }
            return null;
        }
        return null;
    }

    private static string ConvertX11(Context c, string path, string role)
    {
        var bySize = XCursorReader.Read(path);
        if (bySize.Count == 0) throw new InvalidDataException("no images in " + path);
        bool animated = bySize.Values.Any(l => l.Count > 1);
        var anim = XCursorReader.ToAnimation(bySize, animated ? c.AniKeep : c.Keep);
        for (int k = 0; k < anim.DelaysMs.Count; k++)
            if (anim.DelaysMs[k] <= 0) anim.DelaysMs[k] = 50;
        string name = role + (anim.IsAnimated ? ".ani" : ".cur");
        WindowsCursorWriter.Write(Path.Combine(c.OutDir, name), anim);
        return name;
    }

    // ---- Per-role specs ----------------------------------------------------------------------------

    private static void ApplySpec(Context c, int role, string spec)
    {
        var tokens = spec.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        switch (tokens[0])
        {
            case "none":
                c.Produced[role] = null;
                break;
            case "file":
                c.Produced[role] = CopyCursor(c, Path.Combine(c.BaseDir, string.Join(" ", tokens.Skip(1))), Roles[role]);
                c.Mapped.Add(Roles[role] + "=" + string.Join(" ", tokens.Skip(1)));
                break;
            case "x11":
                string path = ResolveX11(c.SourceDir, tokens[1]) ?? throw new FileNotFoundException("x11 cursor not found: " + tokens[1]);
                c.Produced[role] = ConvertX11(c, path, Roles[role]);
                c.Mapped.Add(Roles[role] + "=" + tokens[1]);
                break;
            case "png":
                c.Produced[role] = BuildPng(c, role, tokens);
                break;
            default:
                throw new FormatException($"bad spec for {Roles[role]}: {spec}");
        }
    }

    /// <summary>
    /// png &lt;file&gt; [hot=x,y|tl|top|center] [spin=frames,ms] [seq=a;b;c] [delay=ms] [pixel] [flip] [rotate=deg] [sizes=32,48,64]
    /// </summary>
    private static string BuildPng(Context c, int role, List<string> tokens)
    {
        string pngRoot = Path.Combine(c.Root, (c.Recipe.Get("pngroot") ?? "").Replace('/', '\\'));
        var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string t in tokens.Skip(2))
        {
            var kv = t.Split(new[] { '=' }, 2);
            opts[kv[0]] = kv.Length > 1 ? kv[1] : "";
        }
        bool pixel = opts.ContainsKey("pixel") || c.Recipe.Get("pixel") == "true";
        var sizes = ParseInts(opts.TryGetValue("sizes", out var sz) ? sz : c.Recipe.Get("pngsizes") ?? "32,48,64");

        var names = opts.TryGetValue("seq", out var seq) ? seq.Split(';').ToList() : new List<string> { tokens[1] };
        var frames = names.Select(n => LoadPng(Path.Combine(pngRoot, n.EndsWith(".png") ? n : n + ".png"))).ToList();

        string hot = opts.TryGetValue("hot", out var h) ? h : "tl";
        var (hx, hy) = FindHotspot(frames[0], hot);
        foreach (var f in frames)
        {
            f.HotX = hx;
            f.HotY = hy;
        }
        if (opts.ContainsKey("flip")) frames = frames.Select(FlipX).ToList();
        if (opts.TryGetValue("rotate", out var rot)) frames = frames.Select(f => Rotate(f, float.Parse(rot, CultureInfo.InvariantCulture), pixel)).ToList();

        int delay = opts.TryGetValue("delay", out var dl) ? int.Parse(dl) : 120;
        if (opts.TryGetValue("spin", out var spin))
        {
            var parts = ParseInts(spin);
            var source = frames[0];
            frames = Enumerable.Range(0, parts[0]).Select(k => Rotate(source, 360f * k / parts[0], pixel)).ToList();
            delay = parts.Count > 1 ? parts[1] : 50;
        }

        var anim = new CursorAnimation();
        foreach (var f in frames)
        {
            anim.Frames.Add(sizes.Select(s => f.Resize(s, pixel)).ToList());
            anim.DelaysMs.Add(delay);
        }
        string name = Roles[role] + (anim.IsAnimated ? ".ani" : ".cur");
        WindowsCursorWriter.Write(Path.Combine(c.OutDir, name), anim);
        c.Mapped.Add(Roles[role] + "=" + string.Join(";", names));
        return name;
    }

    private static CursorImage LoadPng(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("png not found: " + path);
        // Decode from memory: GDI+ opens files itself and doesn't understand \\?\ paths.
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        using var bmp = new Bitmap(stream);
        return CursorImage.FromBitmap(bmp, 0, 0);
    }

    private static (int, int) FindHotspot(CursorImage img, string mode)
    {
        int w = img.Width, h = img.Height;
        bool Solid(int x, int y) => (uint)img.Pixels[y * w + x] >> 24 > 128;
        switch (mode)
        {
            case "tl":
            {
                int best = int.MaxValue, bx = 0, by = 0;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (Solid(x, y) && x + y < best)
                        {
                            best = x + y;
                            bx = x;
                            by = y;
                        }
                return (bx, by);
            }
            case "bl":
            {
                int best = int.MinValue, bx = 0, by = h - 1;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (Solid(x, y) && y - x > best)
                        {
                            best = y - x;
                            bx = x;
                            by = y;
                        }
                return (bx, by);
            }
            case "top":
                for (int y = 0; y < h; y++)
                {
                    var xs = Enumerable.Range(0, w).Where(x => Solid(x, y)).ToList();
                    if (xs.Count > 0) return ((int)Math.Round(xs.Average()), y);
                }
                return (w / 2, 0);
            case "center":
            {
                int minX = w, minY = h, maxX = 0, maxY = 0;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (Solid(x, y))
                        {
                            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                        }
                return ((minX + maxX) / 2, (minY + maxY) / 2);
            }
            default:
                var p = ParseInts(mode);
                return (p[0], p[1]);
        }
    }

    private static CursorImage FlipX(CursorImage src)
    {
        var dst = new CursorImage(src.Width, src.Height) { HotX = src.Width - 1 - src.HotX, HotY = src.HotY };
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                dst.Pixels[y * src.Width + (src.Width - 1 - x)] = src.Pixels[y * src.Width + x];
        return dst;
    }

    private static CursorImage Rotate(CursorImage src, float degrees, bool pixel)
    {
        int w = src.Width, h = src.Height;
        using var bmp = src.ToBitmap();
        using var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.InterpolationMode = pixel ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.TranslateTransform(w / 2f, h / 2f);
            g.RotateTransform(degrees);
            g.TranslateTransform(-w / 2f, -h / 2f);
            g.DrawImage(bmp, 0, 0, w, h);
        }
        double rad = degrees * Math.PI / 180, dx = src.HotX - w / 2.0, dy = src.HotY - h / 2.0;
        int hx = (int)Math.Round(w / 2.0 + dx * Math.Cos(rad) - dy * Math.Sin(rad));
        int hy = (int)Math.Round(h / 2.0 + dx * Math.Sin(rad) + dy * Math.Cos(rad));
        return CursorImage.FromBitmap(dst, Math.Max(0, Math.Min(w - 1, hx)), Math.Max(0, Math.Min(h - 1, hy)));
    }

    // ---- Derived neon variants ---------------------------------------------------------------------

    private static void Derive(Context c, string baseDir)
    {
        var baseIni = IniFile.Read(Path.Combine(baseDir, "pack.ini"));
        var neon = ColorTranslator.FromHtml(c.Recipe.Get("neon") ?? throw new FormatException("derive needs neon=#RRGGBB"));
        double shrink = double.Parse(c.Recipe.Get("shrink") ?? "0.86", CultureInfo.InvariantCulture);
        var done = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < RoleCount; i++)
        {
            if (!baseIni.TryGetValue(Roles[i], out var file) || file.Length == 0) continue;
            if (!done.ContainsKey(file))
            {
                byte[] data = File.ReadAllBytes(Path.Combine(baseDir, file));
                File.WriteAllBytes(Path.Combine(c.OutDir, file), NeonFilter.Transform(data, neon, shrink));
                done[file] = file;
            }
            c.Produced[i] = file;
        }
    }

    internal static List<int> ParseInts(string text) =>
        text.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToList();

    private static int RoleIndex(string name)
    {
        int i = Array.FindIndex(Roles, n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i : throw new FormatException("unknown role " + name);
    }
}

/// <summary>Turns a dark cursor with a light outline into a glowing neon version.</summary>
internal static class NeonFilter
{
    public static byte[] Transform(byte[] file, Color neon, double shrink)
    {
        if (WindowsCursorFiles.IsAni(file))
        {
            var ani = WindowsCursorFiles.ReadAni(file);
            ani.Frames = ani.Frames.Select(f => f.Select(e => Apply(e, neon, shrink)).ToList()).ToList();
            return WindowsCursorFiles.WriteAni(ani);
        }
        return WindowsCursorFiles.WriteGroup(WindowsCursorFiles.ReadGroup(file, 0, file.Length).Select(e => Apply(e, neon, shrink)));
    }

    private static WindowsCursorFiles.Entry Apply(WindowsCursorFiles.Entry entry, Color neon, double shrink)
    {
        var img = Shrink(WindowsCursorFiles.Decode(entry), shrink);
        int w = img.Width, h = img.Height, n = w * h;
        var core = new int[n];
        var mask = new float[n];
        for (int p = 0; p < n; p++)
        {
            uint px = (uint)img.Pixels[p];
            int a = (int)(px >> 24);
            if (a == 0) continue;
            double lum = (0.2126 * ((px >> 16) & 255) + 0.7152 * ((px >> 8) & 255) + 0.0722 * (px & 255)) / 255;
            Color color;
            if (lum > 0.5)
            {
                double t = (lum - 0.5) / 0.5;
                color = Mix(neon, Color.White, 0.12 + 0.38 * t);
                mask[p] = a / 255f;
            }
            else
            {
                color = Mix(Color.FromArgb(9, 9, 13), neon, 0.06);
            }
            core[p] = (a << 24) | (color.R << 16) | (color.G << 8) | color.B;
        }

        var glow = Blur(mask, w, h, Math.Max(1, (int)Math.Round(w * 0.03)));
        var result = new CursorImage(w, h) { HotX = img.HotX, HotY = img.HotY };
        for (int p = 0; p < n; p++)
        {
            double ga = Math.Min(1, glow[p] * 2.2) * 0.85;
            double ca = ((uint)core[p] >> 24) / 255.0;
            double oa = ca + ga * (1 - ca);
            if (oa <= 0.003)
            {
                result.Pixels[p] = 0;
                continue;
            }
            int Channel(int shift, int neonValue) =>
                (int)Math.Round((((core[p] >> shift) & 255) * ca + neonValue * ga * (1 - ca)) / oa);
            result.Pixels[p] = ((int)Math.Round(oa * 255) << 24) | (Channel(16, neon.R) << 16) | (Channel(8, neon.G) << 8) | Channel(0, neon.B);
        }
        return new WindowsCursorFiles.Entry
        {
            Width = w,
            Height = h,
            HotX = result.HotX,
            HotY = result.HotY,
            Data = WindowsCursorWriter.EncodePng(result),
        };
    }

    private static CursorImage Shrink(CursorImage src, double s)
    {
        if (s >= 0.999) return src;
        int w = src.Width, h = src.Height;
        float ox = (float)(w * (1 - s) / 2), oy = (float)(h * (1 - s) / 2);
        using var bmp = src.ToBitmap();
        using var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(bmp, new RectangleF(ox, oy, (float)(w * s), (float)(h * s)));
        }
        return CursorImage.FromBitmap(dst, (int)Math.Round(ox + src.HotX * s), (int)Math.Round(oy + src.HotY * s));
    }

    private static float[] Blur(float[] src, int w, int h, int radius)
    {
        var a = (float[])src.Clone();
        var b = new float[a.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sum = 0;
                    for (int k = -radius; k <= radius; k++) sum += a[y * w + Math.Max(0, Math.Min(w - 1, x + k))];
                    b[y * w + x] = sum / (2 * radius + 1);
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sum = 0;
                    for (int k = -radius; k <= radius; k++) sum += b[Math.Max(0, Math.Min(h - 1, y + k)) * w + x];
                    a[y * w + x] = sum / (2 * radius + 1);
                }
        }
        return a;
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));
}
