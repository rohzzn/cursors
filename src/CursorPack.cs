using System;
using System.IO;

namespace Cursors;

internal enum PackKind
{
    Windows,
    Bundled,
    Library,
    User,
    Previous,
    Catalog, // listed in the Community catalog; downloaded the first time it's applied
}

/// <summary>A full cursor scheme. Role order matches the Windows scheme string format.</summary>
internal sealed class CursorPack
{
    public const int RoleCount = 17;

    public const int Arrow = 0, Help = 1, AppStarting = 2, Wait = 3, Crosshair = 4, IBeam = 5, NWPen = 6, No = 7,
        SizeNS = 8, SizeWE = 9, SizeNWSE = 10, SizeNESW = 11, SizeAll = 12, UpArrow = 13, Hand = 14, Pin = 15, Person = 16;

    public static readonly string[] RegistryNames =
    {
        "Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam", "NWPen", "No",
        "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow", "Hand", "Pin", "Person",
    };

    public string Id { get; set; }

    /// <summary>Name written to the registry and shown in Mouse Properties.</summary>
    public string SchemeName { get; set; }

    /// <summary>Name shown on the card.</summary>
    public string Label { get; set; }

    public PackKind Kind { get; set; }

    public string Category { get; set; }

    /// <summary>Position within its category; ties sort by label.</summary>
    public int Order { get; set; }

    public string Author { get; set; }
    public string License { get; set; }
    public string Url { get; set; }

    /// <summary>Community catalog: downloads on the hosting site, and preview image ids for the arrow, link, text and busy cursors.</summary>
    public int Downloads { get; set; }
    public string[] CatalogPreviews { get; set; }

    /// <summary>Folder that holds the pack's files, when it has one of its own.</summary>
    public string Folder { get; set; }

    /// <summary>Bundled packs are copied here before they are applied, so Windows never points into the app folder.</summary>
    public string InstallFolder { get; set; }

    public bool IsWindowsDefault { get; set; }

    /// <summary>Raw registry values per role (may contain environment variables). Null or empty = not provided.</summary>
    public string[] Values { get; } = new string[RoleCount];

    /// <summary>Where each role's file can be read from when that differs from the registry value (bundled packs).</summary>
    public string[] SourceFiles { get; } = new string[RoleCount];

    public CursorPreview Preview { get; set; }

    /// <summary>Small previews of a few other roles, decoded on first hover.</summary>
    public CursorPreview[] RolePreviews { get; set; }

    public static string Expand(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
    }

    public string PathFor(int role) => Expand(Values[role]);

    /// <summary>A readable file for the role, or null.</summary>
    public string SourcePathFor(int role)
    {
        string p = SourceFiles[role] ?? PathFor(role);
        return p != null && File.Exists(p) ? p : null;
    }

    public string PreviewPath
    {
        get
        {
            string arrow = SourcePathFor(Arrow);
            if (arrow != null) return arrow;
            for (int i = 0; i < RoleCount; i++)
            {
                string p = SourcePathFor(i);
                if (p != null) return p;
            }
            return null;
        }
    }

    public int AvailableRoleCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < RoleCount; i++)
                if (SourcePathFor(i) != null) n++;
            return n;
        }
    }

    public override string ToString() => Label;
}
