using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Cursors;

/// <summary>
/// The Community section: popular cursor sets on rw-designer.com, listed in Packs\catalog.tsv by
/// tools\update-catalog.ps1. Only names and links ship with the app. A set is downloaded from the site the first time
/// it's applied, and preview images are fetched as cards come into view, then cached in %LOCALAPPDATA%\Cursors\Catalog.
/// </summary>
internal static class CommunityCatalog
{
    public const string Category = "Community";
    private const long MaxImageBytes = 512 * 1024;

    /// <summary>The site the catalog points at.</summary>
    public static string SiteRoot { get; set; } = "https://www.rw-designer.com";

    public static string CatalogFile { get; set; } = Path.Combine(AppPaths.BundledSourceDir, "catalog.tsv");

    public static string ImageDir => Path.Combine(AppPaths.DataDir, "Catalog");

    public static string SetPageUrl(string slug) => SiteRoot + "/cursor-set/" + slug;

    public static List<CursorPack> Load()
    {
        var packs = new List<CursorPack>();
        string[] lines;
        try
        {
            if (!File.Exists(CatalogFile)) return packs;
            lines = File.ReadAllLines(CatalogFile, Encoding.UTF8);
        }
        catch
        {
            return packs;
        }

        string[] header = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            string[] fields = line.Split('\t');
            if (header == null)
            {
                header = fields;
                continue;
            }
            string Get(string column)
            {
                int i = Array.IndexOf(header, column);
                return i >= 0 && i < fields.Length && fields[i].Trim().Length > 0 ? fields[i].Trim() : null;
            }

            string slug = Get("slug"), name = Get("name"), arrow = Get("arrow");
            if (slug == null || name == null || !Regex.IsMatch(slug, @"^[\w.~-]+$") || !IsImageId(arrow) || !seen.Add(slug)) continue;
            packs.Add(new CursorPack
            {
                Id = "web:" + slug,
                Kind = PackKind.Catalog,
                SchemeName = name,
                Label = name,
                Category = Category,
                Order = packs.Count,
                Author = Get("author"),
                License = Get("license"),
                Url = SetPageUrl(slug),
                Downloads = int.TryParse(Get("downloads"), out int downloads) ? downloads : 0,
                CatalogCursors = int.TryParse(Get("cursors"), out int cursors) ? cursors : 0,
                CatalogRoles = int.TryParse(Get("roles"), out int roles) ? roles : 0,
                CatalogAnimated = int.TryParse(Get("animated"), out int animated) ? animated : 0,
                TopRated = Get("toprated") == "1",
                CatalogPreviews = new[] { arrow, ImageIdOrNull(Get("link")), ImageIdOrNull(Get("text")), ImageIdOrNull(Get("busy")) },
            });
        }
        return packs;
    }

    public static string FormatDownloads(int downloads) =>
        downloads.ToString("N0", CultureInfo.CurrentCulture) + (downloads == 1 ? " download" : " downloads");

    /// <summary>Public domain and attribution-only licenses, which let anyone share and reuse a set.</summary>
    public static bool IsOpenLicense(string license) =>
        license != null && (license.IndexOf("Public Domain", StringComparison.OrdinalIgnoreCase) >= 0
                            || license.Equals("Attribution Required (CC by)", StringComparison.OrdinalIgnoreCase)
                            || license.IndexOf("Free Art", StringComparison.OrdinalIgnoreCase) >= 0);

    private static bool IsImageId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 12) return false;
        foreach (char c in id)
            if (c < '0' || c > '9') return false;
        return true;
    }

    private static string ImageIdOrNull(string id) => IsImageId(id) ? id : null;

    /// <summary>A cached preview image, or null if it hasn't been downloaded yet.</summary>
    public static string CachedImage(string id)
    {
        if (!IsImageId(id)) return null;
        string path = Path.Combine(ImageDir, id + ".png");
        return File.Exists(path) ? path : null;
    }

    /// <summary>The site's rendering of one cursor, downloaded the first time. Null when offline or unavailable.</summary>
    public static string FetchImage(string id)
    {
        if (!IsImageId(id)) return null;
        string path = Path.Combine(ImageDir, id + ".png");
        if (File.Exists(path)) return path;
        try
        {
            byte[] data = LinkImport.GetBytes(new Uri(SiteRoot + "/cursor-view/" + id + ".png"), MaxImageBytes);
            if (data.Length < 8 || data[0] != 0x89 || data[1] != 'P' || data[2] != 'N' || data[3] != 'G') return null;
            Directory.CreateDirectory(ImageDir);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, data);
            try
            {
                File.Move(temp, path);
            }
            catch (IOException)
            {
                File.Delete(temp); // fetched by another thread at the same time
            }
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }
}
