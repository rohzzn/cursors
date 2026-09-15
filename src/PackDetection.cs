using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Cursors;

/// <summary>A pack found on disk before it is copied into the library.</summary>
internal sealed class DetectedPack
{
    public string Name;
    public string Origin; // the install.inf or folder the pack was detected from
    public readonly string[] Files = new string[CursorPack.RoleCount]; // absolute paths

    public int Count => Files.Count(f => f != null);
}

/// <summary>Finds cursor packs in folders: install.inf first, then filename heuristics.</summary>
internal static class PackDetection
{
    private static readonly string[] CursorExtensions = { ".cur", ".ani" };

    // Ordered: earlier rules win, so specific names are matched before generic ones.
    private static readonly KeyValuePair<int, string[]>[] Rules =
    {
        Rule(CursorPack.AppStarting, "appstarting", "appstart", "appstrt", "workinginbackground", "background", "working", "progress", "work"),
        Rule(CursorPack.Help, "helpselect", "helpsel", "help", "question"),
        Rule(CursorPack.Wait, "busy", "wait", "loading", "hourglass"),
        Rule(CursorPack.Crosshair, "precisionselect", "precision", "crosshair", "cross"),
        Rule(CursorPack.IBeam, "textselect", "ibeam", "beam", "text"),
        Rule(CursorPack.NWPen, "handwriting", "nwpen", "pencil", "draft", "pen", "write"),
        Rule(CursorPack.No, "unavailable", "unavailiable", "unavail", "nodrop", "notallowed", "forbidden", "denied", "no"),
        Rule(CursorPack.SizeNWSE, "diagonalresize1", "diagonal1", "dgn1", "dng1", "diag1", "nwse", "=size2"),
        Rule(CursorPack.SizeNESW, "diagonalresize2", "diagonal2", "dgn2", "dng2", "diag2", "nesw", "=size1"),
        Rule(CursorPack.SizeNS, "verticalresize", "vertical", "vert", "sizens", "=size4", "ns"),
        Rule(CursorPack.SizeWE, "horizontalresize", "horizontal", "horiz", "horz", "sizewe", "=size3", "ew", "we"),
        Rule(CursorPack.SizeAll, "sizeall", "move"),
        Rule(CursorPack.UpArrow, "alternateselect", "alternate", "uparrow", "up", "alt"),
        Rule(CursorPack.Pin, "locationselect", "location", "pin"),
        Rule(CursorPack.Person, "personselect", "person"),
        Rule(CursorPack.Hand, "linkselect", "link", "hand"),
        Rule(CursorPack.Arrow, "normalselect", "normal", "arrow", "pointer", "default", "select", "cursor"),
    };

    private static KeyValuePair<int, string[]> Rule(int role, params string[] keys) => new(role, keys);

    public static bool IsCursorFile(string path) =>
        CursorExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static int GuessRole(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return -1;
        // Cursor sites name files "<picture> - <role>" (e.g. "RedStone Torch - Move - Alternate Select"), so the part
        // after the last " - " says what the cursor is for.
        int dash = name.LastIndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
        {
            int role = GuessRoleFromName(name.Substring(dash + 3));
            if (role >= 0) return role;
        }
        return GuessRoleFromName(name);
    }

    private static int GuessRoleFromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return -1;
        string spaced = Regex.Replace(name, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant();
        var tokens = new HashSet<string>(Regex.Split(spaced, "[^a-z0-9]+").Where(t => t.Length > 0));
        tokens.UnionWith(Regex.Split(spaced, "[^a-z]+").Where(t => t.Length > 0));
        string compact = new string(spaced.Where(char.IsLetterOrDigit).ToArray());

        // Short keys, and keys marked with '=', only match whole words; longer keys match anywhere.
        foreach (var rule in Rules)
            foreach (string key in rule.Value)
                if (key[0] == '=' ? tokens.Contains(key.Substring(1)) : key.Length >= 4 ? compact.Contains(key) : tokens.Contains(key))
                    return rule.Key;
        return -1;
    }

    /// <summary>Scans a folder tree for packs. Each install.inf is a pack; otherwise each folder of cursor files is.</summary>
    public static List<DetectedPack> Scan(string root, string nameHint)
    {
        var result = new List<DetectedPack>();
        foreach (string inf in SafeEnumerate(root, "*.inf").Take(64))
        {
            var pack = ParseInf(inf, root);
            if (pack != null && pack.Count > 0) result.Add(pack);
        }
        if (result.Count > 0) return result;

        var groups = SafeEnumerate(root, "*.*").Where(IsCursorFile)
            .GroupBy(f => Path.GetDirectoryName(f), StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            string dir = group.Key;
            string name = string.Equals(dir.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                ? nameHint
                : Path.GetFileName(dir);
            var pack = FromFiles(group, name);
            if (pack.Count > 0) result.Add(pack);
        }
        return result;
    }

    public static DetectedPack FromFiles(IEnumerable<string> files, string name)
    {
        var list = files.ToList();
        var pack = new DetectedPack { Name = name, Origin = list.Count > 0 ? Path.GetDirectoryName(list[0]) : null };
        var leftovers = new List<string>();
        // Prefer animated variants when both .ani and .cur exist for a role.
        foreach (string file in list.OrderBy(f => Path.GetExtension(f).Equals(".ani", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                                    .ThenBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            int role = GuessRole(Path.GetFileNameWithoutExtension(file));
            if (role >= 0 && pack.Files[role] == null) pack.Files[role] = file;
            else leftovers.Add(file);
        }
        if (pack.Files[CursorPack.Arrow] == null && leftovers.Count > 0)
            pack.Files[CursorPack.Arrow] = leftovers[0];
        return pack;
    }

    public static DetectedPack ParseInf(string infPath, string searchRoot)
    {
        string[] lines;
        try
        {
            using var reader = new StreamReader(infPath, Encoding.Default, detectEncodingFromByteOrderMarks: true);
            lines = JoinContinuations(reader.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
        }
        catch
        {
            return null;
        }

        var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var body = new List<string>();
        string section = "";
        foreach (string rawLine in lines)
        {
            string line = StripComment(rawLine).Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                section = line.Substring(1, line.Length - 2).Trim();
                continue;
            }
            if (section.Equals("Strings", StringComparison.OrdinalIgnoreCase))
            {
                int eq = line.IndexOf('=');
                if (eq > 0) strings[line.Substring(0, eq).Trim()] = Unquote(line.Substring(eq + 1).Trim());
            }
            else
            {
                body.Add(line);
            }
        }

        string Expand(string s)
        {
            for (int pass = 0; pass < 8 && s.IndexOf('%') >= 0; pass++)
            {
                string next = Regex.Replace(s, "%([^%]*)%", m =>
                    m.Groups[1].Value.Length == 0 ? "%" : strings.TryGetValue(m.Groups[1].Value, out var v) ? v : "");
                if (next == s) break;
                s = next;
            }
            return s;
        }

        string dir = Path.GetDirectoryName(infPath);
        var pack = new DetectedPack { Origin = infPath };
        var extras = new List<string>(); // scheme entries beyond the 17 Windows roles
        string schemeName = null;

        foreach (string line in body)
        {
            var fields = SplitFields(line);
            if (fields.Count < 3) continue;
            string key = Expand(fields[1]).Trim().Trim('\\');
            if (key.Equals(@"Control Panel\Cursors\Schemes", StringComparison.OrdinalIgnoreCase) && fields.Count >= 5)
            {
                schemeName ??= Expand(fields[2]).Trim();
                string list = Expand(string.Join(",", fields.Skip(4)));
                string[] entries = list.Split(',');
                for (int i = 0; i < entries.Length; i++)
                {
                    if (i < CursorPack.RoleCount) pack.Files[i] ??= Resolve(entries[i], dir, searchRoot);
                    else if (Resolve(entries[i], dir, searchRoot) is string extra) extras.Add(extra);
                }
            }
            else if (key.Equals(@"Control Panel\Cursors", StringComparison.OrdinalIgnoreCase))
            {
                string valueName = Expand(fields[2]).Trim();
                string value = fields.Count >= 5 ? Expand(fields[4]) : "";
                if (valueName.Length == 0)
                {
                    if (value.Trim().Length > 0) schemeName ??= value.Trim();
                    continue;
                }
                int role = Array.FindIndex(CursorPack.RegistryNames, n => n.Equals(valueName, StringComparison.OrdinalIgnoreCase));
                if (role >= 0) pack.Files[role] ??= Resolve(value, dir, searchRoot);
            }
        }

        if (pack.Count == 0)
        {
            // No registry lines: fall back to the conventional [Strings] keys (pointer, help, work, busy...).
            foreach (var kv in strings)
            {
                if (!IsCursorFile(kv.Value)) continue;
                string k = kv.Key.ToLowerInvariant();
                int role = k == "hand" ? CursorPack.NWPen : k == "work" ? CursorPack.AppStarting : GuessRole(k);
                if (role < 0) role = GuessRole(Path.GetFileNameWithoutExtension(kv.Value));
                if (role >= 0) pack.Files[role] ??= Resolve(kv.Value, dir, searchRoot);
            }
        }

        RepairRoleOrder(pack, extras);
        if (schemeName == null && strings.TryGetValue("SCHEME_NAME", out var sn)) schemeName = sn;
        pack.Name = string.IsNullOrWhiteSpace(schemeName) ? Path.GetFileName(dir) : schemeName.Trim();
        return pack;
    }

    /// <summary>
    /// Some published install.inf files list roles out of order (e.g. an entry moved to the end shifts everything
    /// after it). A file whose name clearly states its role wins over its position; the rest keep their positions.
    /// </summary>
    private static void RepairRoleOrder(DetectedPack pack, List<string> extras)
    {
        var repaired = new string[CursorPack.RoleCount];
        var unplaced = new List<int>();
        for (int i = 0; i < CursorPack.RoleCount; i++)
        {
            string file = pack.Files[i];
            if (file == null) continue;
            int guess = GuessRole(Path.GetFileNameWithoutExtension(file));
            if (guess >= 0 && repaired[guess] == null) repaired[guess] = file;
            else unplaced.Add(i);
        }
        foreach (string file in extras)
        {
            int guess = GuessRole(Path.GetFileNameWithoutExtension(file));
            if (guess >= 0 && repaired[guess] == null && Array.IndexOf(repaired, file) < 0) repaired[guess] = file;
        }
        foreach (int i in unplaced)
            if (repaired[i] == null && Array.IndexOf(repaired, pack.Files[i]) < 0) repaired[i] = pack.Files[i];
        Array.Copy(repaired, pack.Files, CursorPack.RoleCount);
    }

    private static string Resolve(string entry, string dir, string searchRoot)
    {
        string fileName;
        try
        {
            fileName = Path.GetFileName(entry.Trim().Trim('"').Trim());
        }
        catch
        {
            return null;
        }
        if (string.IsNullOrEmpty(fileName)) return null;

        string direct = Path.Combine(dir, fileName);
        if (File.Exists(direct)) return direct;
        return SafeEnumerate(searchRoot ?? dir, fileName).FirstOrDefault();
    }

    private static IEnumerable<string> SafeEnumerate(string root, string pattern)
    {
        var pending = new Stack<KeyValuePair<string, int>>();
        pending.Push(new KeyValuePair<string, int>(root, 0));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            string[] files = Array.Empty<string>(), dirs = Array.Empty<string>();
            try
            {
                files = Directory.GetFiles(current.Key, pattern);
                if (current.Value < 6) dirs = Directory.GetDirectories(current.Key);
            }
            catch { }
            foreach (string f in files) yield return f;
            foreach (string d in dirs) pending.Push(new KeyValuePair<string, int>(d, current.Value + 1));
        }
    }

    private static string[] JoinContinuations(string[] lines)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        foreach (string line in lines)
        {
            string trimmed = line.TrimEnd();
            if (trimmed.EndsWith("\\") && !trimmed.EndsWith("\\\\"))
            {
                sb.Append(trimmed, 0, trimmed.Length - 1);
                continue;
            }
            sb.Append(line);
            result.Add(sb.ToString());
            sb.Clear();
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        return result.ToArray();
    }

    private static string StripComment(string line)
    {
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == ';' && !quoted) return line.Substring(0, i);
        }
        return line;
    }

    private static string Unquote(string s)
    {
        if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2);
        return s.Replace("\"\"", "\"");
    }

    private static List<string> SplitFields(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == ',' && !quoted)
            {
                fields.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        fields.Add(sb.ToString().Trim());
        return fields;
    }
}
