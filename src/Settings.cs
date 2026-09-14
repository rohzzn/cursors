using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Cursors;

internal static class AppPaths
{
    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cursors");

    public static readonly string PacksDir = Path.Combine(DataDir, "Packs");
    public static readonly string BundledInstallDir = Path.Combine(DataDir, "Bundled");
    public static readonly string BundledSourceDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Packs");
    public static readonly string SettingsFile = Path.Combine(DataDir, "settings.ini");
    public static readonly string PreviousFile = Path.Combine(DataDir, "previous.ini");
}

/// <summary>Tiny key=value store. Holds only UI state; the cursor itself persists in the Windows registry.</summary>
internal static class IniFile
{
    public static Dictionary<string, string> Read(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(path)) return values;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq > 0) values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
        }
        catch { }
        return values;
    }

    public static void Write(string path, IEnumerable<KeyValuePair<string, string>> values)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var sb = new StringBuilder();
            foreach (var kv in values)
                if (kv.Value != null) sb.Append(kv.Key).Append('=').Append(kv.Value.Replace("\r", "").Replace("\n", "")).Append("\r\n");
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch { }
    }
}
