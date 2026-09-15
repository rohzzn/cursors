using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

namespace Cursors;

internal sealed class ImportResult
{
    public readonly List<string> AddedIds = new();
    public readonly List<string> ExistingIds = new();
}

/// <summary>Where an import came from; saved in the pack's pack.ini so its credits show in the app.</summary>
internal sealed class ImportDetails
{
    public string Name, Author, License, Url;

    /// <summary>File name to cursor role, as tagged by the site the pack came from.</summary>
    public Dictionary<string, int> RoleFiles;
}

/// <summary>
/// The library shown in the grid: the Windows styles, installed user schemes, packs imported into
/// %LOCALAPPDATA%\Cursors\Packs, and a snapshot of whatever was active before the app was first used.
/// </summary>
internal sealed class PackLibrary
{
    private const string SystemSchemesKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\Cursors\Schemes";
    private const string UserSchemesKey = @"Control Panel\Cursors\Schemes";
    private const string PackIni = "pack.ini";
    private const long MaxEntryBytes = 32L * 1024 * 1024;

    public const string WindowsCategory = "Windows", AddedCategory = "Added";

    /// <summary>Display order of categories; anything unknown sorts just before packs the user added.</summary>
    public static readonly string[] CategoryOrder =
        { WindowsCategory, "Minimal", "macOS", "Retro", "Pixel & Gaming", "Neon", "Glass", "Cute", "Animated", CommunityCatalog.Category, AddedCategory };

    public IReadOnlyList<CursorPack> Packs { get; private set; } = Array.Empty<CursorPack>();
    public CursorPack WindowsDefault { get; private set; }

    public static int CategoryRank(string category)
    {
        int i = Array.FindIndex(CategoryOrder, c => c.Equals(category, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i * 2 : CategoryOrder.Length * 2 - 3;
    }

    public static PackLibrary Load()
    {
        var packs = new List<CursorPack>();
        var windowsDefault = LoadWindowsSchemes(packs);
        LoadBundled(packs);
        LoadLibraryPacks(packs);
        LoadUserSchemes(packs);
        LoadPrevious(packs, windowsDefault);
        LoadCatalog(packs);
        return new PackLibrary
        {
            WindowsDefault = windowsDefault,
            Packs = packs.OrderBy(p => CategoryRank(p.Category)).ThenBy(p => p.Order)
                .ThenBy(p => p.Label, StringComparer.CurrentCultureIgnoreCase).ToList(),
        };
    }

    // ---- Bundled library ---------------------------------------------------------------------------

    /// <summary>
    /// Curated packs shipped in the Packs folder next to the exe. Copies already installed under %LOCALAPPDATA% are
    /// used as a fallback, so an applied pack still shows up if the app is later run without its Packs folder.
    /// </summary>
    private static void LoadBundled(List<CursorPack> packs)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in new[] { AppPaths.BundledSourceDir, AppPaths.BundledInstallDir })
        {
            foreach (string dir in SafeDirectories(root))
            {
                string folder = Path.GetFileName(dir);
                if (seen.Contains(folder) || !File.Exists(Path.Combine(dir, PackIni))) continue;
                var values = IniFile.Read(Path.Combine(dir, PackIni));
                string Get(string key) => values.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

                string name = CleanName(Get("name") ?? folder);
                var pack = new CursorPack
                {
                    Id = "bundled:" + folder,
                    Kind = PackKind.Bundled,
                    SchemeName = name,
                    Label = name,
                    Category = Get("category") ?? "Minimal",
                    Order = int.TryParse(Get("order"), out int order) ? order : 1000,
                    Author = Get("author"),
                    License = Get("license"),
                    Url = Get("url"),
                    Folder = dir,
                    InstallFolder = Path.Combine(AppPaths.BundledInstallDir, folder),
                };
                for (int i = 0; i < CursorPack.RoleCount; i++)
                {
                    string file = Get(CursorPack.RegistryNames[i]);
                    if (file == null || Path.GetFileName(file) != file) continue;
                    pack.SourceFiles[i] = Path.Combine(dir, file);
                    pack.Values[i] = Path.Combine(pack.InstallFolder, file);
                }
                if (pack.PreviewPath == null) continue;
                seen.Add(folder);
                packs.Add(pack);
            }
        }
    }

    /// <summary>Copies a bundled pack's files to its install folder (only files that changed).</summary>
    public static void Install(CursorPack pack)
    {
        if (pack.Kind != PackKind.Bundled || pack.InstallFolder == null || pack.Folder == null) return;
        string source = Path.GetFullPath(pack.Folder).TrimEnd('\\');
        string target = Path.GetFullPath(pack.InstallFolder).TrimEnd('\\');
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase)) return;

        Directory.CreateDirectory(target);
        var files = pack.SourceFiles.Where(f => f != null).Distinct(StringComparer.OrdinalIgnoreCase)
            .Concat(new[] { Path.Combine(source, PackIni) });
        foreach (string file in files)
        {
            var src = new FileInfo(file);
            if (!src.Exists) continue;
            var dst = new FileInfo(Path.Combine(target, src.Name));
            if (dst.Exists && dst.Length == src.Length && dst.LastWriteTimeUtc == src.LastWriteTimeUtc) continue;
            string temp = dst.FullName + ".tmp";
            File.Copy(src.FullName, temp, true);
            File.SetLastWriteTimeUtc(temp, src.LastWriteTimeUtc);
            if (dst.Exists) File.Delete(dst.FullName);
            File.Move(temp, dst.FullName);
        }
    }

    // ---- Windows styles ------------------------------------------------------------------------

    private static CursorPack LoadWindowsSchemes(List<CursorPack> packs)
    {
        var found = new List<CursorPack>();
        try
        {
            var view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default;
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = hklm.OpenSubKey(SystemSchemesKey);
            if (key != null)
            {
                foreach (string valueName in key.GetValueNames())
                {
                    if (key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string raw) continue;
                    string[] parts = raw.Split(',');
                    string label = null;
                    if (parts.Length > CursorPack.RoleCount && parts[CursorPack.RoleCount].TrimStart().StartsWith("@"))
                        label = Native.LoadIndirectString(string.Join(",", parts.Skip(CursorPack.RoleCount)).Trim());
                    label = string.IsNullOrWhiteSpace(label) ? valueName : label;

                    var pack = new CursorPack { Id = "win:" + valueName, Kind = PackKind.Windows, SchemeName = label, Label = label };
                    FillValues(pack, parts);
                    if (pack.AvailableRoleCount > 0) found.Add(pack);
                }
            }
        }
        catch { }

        CursorPack windowsDefault = null;
        var styles = new Dictionary<string, KeyValuePair<CursorPack, int>>();
        foreach (var pack in found)
        {
            if (!TryWindowsStyle(pack.Values[CursorPack.Arrow], out string style, out int rank)) continue;
            if (style == "aero_arrow" && rank == 0) windowsDefault = pack;
            // Size variants are redundant with Windows' own pointer size setting, so show one card per style.
            if (!styles.TryGetValue(style, out var current) || rank < current.Value)
                styles[style] = new KeyValuePair<CursorPack, int>(pack, rank);
        }

        if (windowsDefault == null) windowsDefault = SyntheticDefault();
        windowsDefault.IsWindowsDefault = true;
        windowsDefault.Category = WindowsCategory;
        windowsDefault.Order = 0;
        packs.Add(windowsDefault);

        foreach (var entry in styles)
        {
            if (entry.Key == "aero_arrow") continue;
            var pack = entry.Value.Key;
            if (entry.Value.Value > 0) pack.Label = Regex.Replace(pack.Label, @"\s*\([^)]*\)\s*$", "");
            pack.Category = WindowsCategory;
            pack.Order = 1;
            packs.Add(pack);
        }
        return windowsDefault;
    }

    private static bool TryWindowsStyle(string arrowValue, out string style, out int rank)
    {
        string name = Path.GetFileNameWithoutExtension(arrowValue ?? "").ToLowerInvariant();
        var m = Regex.Match(name, @"^(aero_arrow|arrow)(?:_(r|i)?(m|l|xl)?)?$");
        style = null;
        rank = 0;
        if (!m.Success) return false;
        style = m.Groups[1].Value + m.Groups[2].Value;
        string size = m.Groups[3].Value;
        rank = size.Length == 0 ? 0
            : m.Groups[1].Value == "aero_arrow" ? (size == "l" ? 1 : 2)
            : (size == "m" ? 1 : 2);
        return true;
    }

    private static CursorPack SyntheticDefault()
    {
        string[] files =
        {
            "aero_arrow.cur", "aero_helpsel.cur", "aero_working.ani", "aero_busy.ani", "", "", "aero_pen.cur", "aero_unavail.cur",
            "aero_ns.cur", "aero_ew.cur", "aero_nwse.cur", "aero_nesw.cur", "aero_move.cur", "aero_up.cur", "aero_link.cur",
            "aero_pin.cur", "aero_person.cur",
        };
        var pack = new CursorPack { Id = "win:default", Kind = PackKind.Windows, SchemeName = "Windows Default", Label = "Windows Default" };
        for (int i = 0; i < files.Length; i++)
            if (files[i].Length > 0) pack.Values[i] = @"%SystemRoot%\cursors\" + files[i];
        return pack;
    }

    // ---- User schemes and snapshot -------------------------------------------------------------

    private static void LoadUserSchemes(List<CursorPack> packs)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UserSchemesKey);
            if (key == null) return;
            var taken = new HashSet<string>(packs.Select(p => p.SchemeName), StringComparer.OrdinalIgnoreCase);
            foreach (string name in key.GetValueNames())
            {
                if (name.Length == 0 || taken.Contains(name)) continue;
                if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string raw) continue;
                var pack = new CursorPack { Id = "user:" + name, Kind = PackKind.User, SchemeName = name, Label = name, Category = AddedCategory };
                FillValues(pack, raw.Split(','));
                string preview = pack.PreviewPath;
                if (preview == null) continue;
                pack.Folder = Path.GetDirectoryName(preview);
                packs.Add(pack);
            }
        }
        catch { }
    }

    /// <summary>Saves the cursors that were active before this app changed anything, once.</summary>
    public static void SnapshotCurrentOnce()
    {
        if (File.Exists(AppPaths.PreviousFile)) return;
        string[] current = CursorScheme.ReadCurrent(out string name);
        var values = new List<KeyValuePair<string, string>> { new("name", name ?? "") };
        if (current != null)
            for (int i = 0; i < current.Length; i++)
                if (current[i] != null) values.Add(new KeyValuePair<string, string>(CursorPack.RegistryNames[i], current[i]));
        IniFile.Write(AppPaths.PreviousFile, values);
    }

    private static void LoadPrevious(List<CursorPack> packs, CursorPack fallback)
    {
        var values = IniFile.Read(AppPaths.PreviousFile);
        if (values.Count == 0) return;

        var pack = new CursorPack { Id = "prev", Kind = PackKind.Previous, Category = AddedCategory, Order = -1 };
        for (int i = 0; i < CursorPack.RoleCount; i++)
            if (values.TryGetValue(CursorPack.RegistryNames[i], out var v) && v.Length > 0) pack.Values[i] = v;
        string preview = pack.PreviewPath;
        if (preview == null) return;

        // Only show the snapshot when no other card already represents those exact cursors.
        var mine = Enumerable.Range(0, CursorPack.RoleCount)
            .Select(i => CursorScheme.Normalize(CursorScheme.Effective(pack, fallback, i))).ToArray();
        foreach (var other in packs)
            if (Enumerable.Range(0, CursorPack.RoleCount).All(i => CursorScheme.Normalize(CursorScheme.Effective(other, fallback, i)) == mine[i]))
                return;

        values.TryGetValue("name", out var name);
        pack.SchemeName = name ?? "";
        pack.Label = string.IsNullOrWhiteSpace(name) || packs.Any(p => string.Equals(p.Label, name, StringComparison.OrdinalIgnoreCase))
            ? "Previous cursors"
            : name;
        pack.Folder = Path.GetDirectoryName(preview);
        packs.Add(pack);
    }

    private static void FillValues(CursorPack pack, string[] parts)
    {
        for (int i = 0; i < CursorPack.RoleCount && i < parts.Length; i++)
        {
            string v = parts[i].Trim();
            pack.Values[i] = v.Length == 0 ? null : v;
        }
    }

    // ---- Community catalog ---------------------------------------------------------------------

    /// <summary>Community sets from the catalog. One that's been downloaded takes its catalog entry's place.</summary>
    private static void LoadCatalog(List<CursorPack> packs)
    {
        var downloaded = new Dictionary<string, CursorPack>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs)
            if (pack.Kind == PackKind.Library && pack.Url != null && !downloaded.ContainsKey(pack.Url)) downloaded[pack.Url] = pack;

        foreach (var entry in CommunityCatalog.Load())
        {
            if (!downloaded.TryGetValue(entry.Url, out var pack))
            {
                packs.Add(entry);
                continue;
            }
            pack.Category = entry.Category;
            pack.Order = entry.Order;
            pack.Downloads = entry.Downloads;
            pack.CatalogPreviews = entry.CatalogPreviews;
            if (entry.Author != null) pack.Author = entry.Author;
        }
    }

    // ---- Imported packs ------------------------------------------------------------------------

    private static void LoadLibraryPacks(List<CursorPack> packs)
    {
        if (!Directory.Exists(AppPaths.PacksDir)) return;
        foreach (string dir in SafeDirectories(AppPaths.PacksDir))
        {
            var pack = ReadLibraryPack(dir);
            if (pack != null && pack.PreviewPath != null) packs.Add(pack);
        }
    }

    private static CursorPack ReadLibraryPack(string dir)
    {
        string folderName = Path.GetFileName(dir);
        var pack = new CursorPack { Id = "lib:" + folderName, Kind = PackKind.Library, Folder = dir, Category = AddedCategory };
        string ini = Path.Combine(dir, PackIni);
        try
        {
            if (File.Exists(ini))
            {
                var values = IniFile.Read(ini);
                pack.SchemeName = values.TryGetValue("name", out var n) && n.Length > 0 ? n : folderName;
                pack.Author = values.TryGetValue("author", out var author) && author.Length > 0 ? author : null;
                pack.License = values.TryGetValue("license", out var license) && license.Length > 0 ? license : null;
                pack.Url = values.TryGetValue("url", out var url) && url.Length > 0 ? url : null;
                for (int i = 0; i < CursorPack.RoleCount; i++)
                    if (values.TryGetValue(CursorPack.RegistryNames[i], out var f) && f.Length > 0)
                        pack.Values[i] = Path.Combine(dir, f);
            }
            else
            {
                // A folder dropped in by hand: detect once and cache the mapping.
                var detected = PackDetection.Scan(dir, folderName).FirstOrDefault();
                if (detected == null) return null;
                pack.SchemeName = CleanName(detected.Name ?? folderName);
                Array.Copy(detected.Files, pack.Values, CursorPack.RoleCount);
                WritePackIni(dir, pack.SchemeName, detected.Files);
            }
        }
        catch
        {
            return null;
        }
        pack.Label = pack.SchemeName;
        return pack;
    }

    public static ImportResult Import(IEnumerable<string> paths, ImportDetails details = null)
    {
        var result = new ImportResult();
        var loose = new List<string>();
        foreach (string path in paths)
        {
            try
            {
                string ext = Path.GetExtension(path);
                if (Directory.Exists(path))
                    AddAll(PackDetection.Scan(path, Path.GetFileName(path.TrimEnd('\\'))), result, details);
                else if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    ImportZip(path, result, details);
                else if (ext.Equals(".inf", StringComparison.OrdinalIgnoreCase))
                    AddAll(new[] { PackDetection.ParseInf(path, Path.GetDirectoryName(path)) }, result, details);
                else if (PackDetection.IsCursorFile(path))
                    loose.Add(path);
            }
            catch { }
        }

        foreach (var group in loose.GroupBy(Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase))
        {
            var files = group.ToList();
            string name = files.Count == 1 ? Path.GetFileNameWithoutExtension(files[0]) : Path.GetFileName(group.Key);
            try
            {
                AddAll(new[] { PackDetection.FromFiles(files, name) }, result, details);
            }
            catch { }
        }
        return result;
    }

    private static void ImportZip(string zipPath, ImportResult result, ImportDetails details)
    {
        string temp = Path.Combine(Path.GetTempPath(), "Cursors-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            string rootFull = Path.GetFullPath(temp).TrimEnd('\\') + "\\";
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zip.Entries)
                {
                    try
                    {
                        bool wanted = PackDetection.IsCursorFile(entry.FullName)
                            || Path.GetExtension(entry.FullName).Equals(".inf", StringComparison.OrdinalIgnoreCase);
                        if (!wanted || entry.Length > MaxEntryBytes) continue;
                        string dest = Path.GetFullPath(Path.Combine(temp, entry.FullName.Replace('/', '\\')));
                        if (!dest.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        entry.ExtractToFile(dest, true);
                    }
                    catch { }
                }
            }
            var found = PackDetection.Scan(temp, Path.GetFileNameWithoutExtension(zipPath));
            if (details?.RoleFiles?.Count > 0) found = ApplyRoleHints(found, temp, details.RoleFiles);
            AddAll(found, result, details);
        }
        finally
        {
            try
            {
                Directory.Delete(temp, true);
            }
            catch { }
        }
    }

    /// <summary>
    /// Uses the roles the hosting site tagged each file with (rw-designer labels every cursor in a set) instead of
    /// guessing from file names. Only for a download that is one pack of loose files; an install.inf stays in charge.
    /// </summary>
    private static List<DetectedPack> ApplyRoleHints(List<DetectedPack> detected, string root, Dictionary<string, int> roleFiles)
    {
        if (detected.Count > 1 || detected.Any(p => p.Origin != null && p.Origin.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))) return detected;
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.GetFiles(root, "*.*", System.IO.SearchOption.AllDirectories))
            if (PackDetection.IsCursorFile(file) && !files.ContainsKey(Path.GetFileName(file))) files[Path.GetFileName(file)] = file;

        var guess = detected.FirstOrDefault();
        var pack = new DetectedPack { Name = guess?.Name, Origin = guess?.Origin ?? root };
        foreach (var hint in roleFiles)
            if (hint.Value >= 0 && hint.Value < CursorPack.RoleCount && pack.Files[hint.Value] == null && files.TryGetValue(hint.Key, out var file))
                pack.Files[hint.Value] = file;
        if (pack.Files[CursorPack.Arrow] == null) return detected;

        // Roles the site doesn't tag (location, person) keep what the file names suggest, using files not already placed.
        if (guess != null)
            for (int i = 0; i < CursorPack.RoleCount; i++)
                if (pack.Files[i] == null && guess.Files[i] != null && Array.IndexOf(pack.Files, guess.Files[i]) < 0 && !roleFiles.ContainsKey(Path.GetFileName(guess.Files[i])))
                    pack.Files[i] = guess.Files[i];
        return new List<DetectedPack> { pack };
    }

    private static void AddAll(IEnumerable<DetectedPack> detected, ImportResult result, ImportDetails details = null)
    {
        var packs = detected.Where(p => p != null && p.Count > 0).ToList();
        // A download holding a single pack takes the name it was published under.
        if (packs.Count == 1 && !string.IsNullOrWhiteSpace(details?.Name)) packs[0].Name = details.Name;
        foreach (var pack in packs) AddToLibrary(pack, result, details);
    }

    private static void AddToLibrary(DetectedPack detected, ImportResult result, ImportDetails details)
    {
        Directory.CreateDirectory(AppPaths.PacksDir);
        string name = CleanName(detected.Name);

        var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string dir in SafeDirectories(AppPaths.PacksDir))
        {
            var existing = ReadLibraryPack(dir);
            if (existing == null) continue;
            if (string.Equals(existing.SchemeName, name, StringComparison.OrdinalIgnoreCase) && SameFiles(existing, detected))
            {
                result.ExistingIds.Add(existing.Id);
                return;
            }
            existingNames.Add(existing.SchemeName);
        }

        string uniqueName = name;
        for (int n = 2; existingNames.Contains(uniqueName); n++) uniqueName = name + " " + n;

        string baseFolder = SafeFolderName(uniqueName);
        string dest = Path.Combine(AppPaths.PacksDir, baseFolder);
        for (int n = 2; Directory.Exists(dest); n++) dest = Path.Combine(AppPaths.PacksDir, baseFolder + " (" + n + ")");
        Directory.CreateDirectory(dest);

        var relative = new string[CursorPack.RoleCount];
        var copied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // file name -> source
        for (int i = 0; i < CursorPack.RoleCount; i++)
        {
            string src = detected.Files[i];
            if (src == null) continue;
            string fileName = Path.GetFileName(src);
            if (copied.TryGetValue(fileName, out var previous) && !previous.Equals(src, StringComparison.OrdinalIgnoreCase))
                fileName = CursorPack.RegistryNames[i] + "_" + fileName;
            if (!copied.ContainsKey(fileName))
            {
                File.Copy(src, Path.Combine(dest, fileName), true);
                copied[fileName] = src;
            }
            relative[i] = fileName;
        }

        WritePackIni(dest, uniqueName, relative, details);
        result.AddedIds.Add("lib:" + Path.GetFileName(dest));
    }

    private static bool SameFiles(CursorPack existing, DetectedPack detected)
    {
        for (int i = 0; i < CursorPack.RoleCount; i++)
        {
            string a = existing.PathFor(i), b = detected.Files[i];
            if (a == null && b == null) continue;
            if (a == null || b == null || !File.Exists(a) || !File.Exists(b)) return false;
            if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        }
        return true;
    }

    private static void WritePackIni(string dir, string name, string[] files, ImportDetails details = null)
    {
        string prefix = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
        var values = new List<KeyValuePair<string, string>> { new("name", name) };
        if (!string.IsNullOrWhiteSpace(details?.Author)) values.Add(new("author", details.Author));
        if (!string.IsNullOrWhiteSpace(details?.License)) values.Add(new("license", details.License));
        if (!string.IsNullOrWhiteSpace(details?.Url)) values.Add(new("url", details.Url));
        for (int i = 0; i < CursorPack.RoleCount; i++)
        {
            string f = files[i];
            if (string.IsNullOrEmpty(f)) continue;
            if (Path.IsPathRooted(f) && f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) f = f.Substring(prefix.Length);
            values.Add(new KeyValuePair<string, string>(CursorPack.RegistryNames[i], f));
        }
        IniFile.Write(Path.Combine(dir, PackIni), values);
    }

    /// <summary>Moves an imported pack to the Recycle Bin. Never touches anything outside the library folder.</summary>
    public static void Remove(CursorPack pack)
    {
        if (pack.Kind != PackKind.Library || pack.Folder == null) return;
        string full = Path.GetFullPath(pack.Folder).TrimEnd('\\');
        string root = Path.GetFullPath(AppPaths.PacksDir).TrimEnd('\\') + "\\";
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(full)) return;
        FileSystem.DeleteDirectory(full, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    private static string CleanName(string name)
    {
        name = Regex.Replace(name ?? "", @"\s+", " ").Trim();
        if (name.Length > 64) name = name.Substring(0, 64).Trim();
        return name.Length == 0 ? "Cursor pack" : name;
    }

    private static string SafeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (safe.Length > 60) safe = safe.Substring(0, 60).Trim(' ', '.');
        return safe.Length == 0 ? "Pack" : safe;
    }

    private static string[] SafeDirectories(string path)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
