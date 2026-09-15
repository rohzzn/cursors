using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Cursors;

internal sealed class LinkImportResult
{
    public ImportResult Import;
    public string Error;
    public string License;
}

/// <summary>
/// Downloads one cursor set the user asked for and adds it to the local library. Accepts a cursor set page
/// (rw-designer.com layout), a single cursor's page, or a direct .zip/.cur/.ani link. Nothing is fetched unless the
/// user pastes or drops a link, and packs imported this way stay on this PC.
/// </summary>
internal static class LinkImport
{
    private const long MaxDownloadBytes = 50L * 1024 * 1024, MaxPageBytes = 4L * 1024 * 1024;
    private const string UserAgent = "Cursors/1.1 (+https://github.com/rohzzn/cursors)";

    static LinkImport()
    {
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; // + TLS 1.3
        }
        catch (NotSupportedException)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }
    }

    public static bool LooksLikeLink(string text) =>
        Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    public static LinkImportResult Import(string link, string author = null)
    {
        if (!LooksLikeLink(link)) return Fail("That isn't a web link");
        var page = new Uri(link.Trim());
        string temp = Path.Combine(Path.GetTempPath(), "Cursors-link-" + Guid.NewGuid().ToString("N"));
        try
        {
            var details = new ImportDetails { Url = page.AbsoluteUri, Author = author ?? SiteName(page) };
            var file = ResolveDownload(page, details, out string error);
            if (file == null) return Fail(error);

            string name = SafeFileName(Uri.UnescapeDataString(Path.GetFileName(file.AbsolutePath)));
            if (!IsCursorDownload(name)) return Fail("That link doesn't lead to a .zip, .cur or .ani file");
            Directory.CreateDirectory(temp);
            string path = Path.Combine(temp, name);
            Download(file, path);
            return new LinkImportResult { Import = PackLibrary.Import(new[] { path }, details), License = details.License };
        }
        catch (WebException ex)
        {
            return Fail(Describe(ex));
        }
        catch (InvalidDataException ex)
        {
            return Fail(ex.Message);
        }
        catch (Exception)
        {
            return Fail("Couldn't import that link");
        }
        finally
        {
            try
            {
                if (Directory.Exists(temp)) Directory.Delete(temp, true);
            }
            catch { }
        }
    }

    private static LinkImportResult Fail(string error) => new() { Error = error };

    private static bool IsCursorDownload(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext == ".zip" || ext == ".cur" || ext == ".ani";
    }

    private static Uri ResolveDownload(Uri page, ImportDetails details, out string error)
    {
        error = null;
        if (IsCursorDownload(page.AbsolutePath)) return page;

        string html = GetPage(page);
        details.Name = PageTitle(html);
        details.License = PageLicense(html);
        details.RoleFiles = PageRoles(html);
        // A set page offers the whole set as one zip; a single cursor's page links just that cursor.
        var link = FindLink(html, page, "/cursor-downloadset/", ".zip") ?? FindLink(html, page, "/cursor-download/", ".cur", ".ani");
        if (link == null) error = "No cursor download was found on that page";
        return link;
    }

    private static Uri FindLink(string html, Uri page, string pathPrefix, params string[] extensions)
    {
        foreach (Match m in Regex.Matches(html, "href\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
        {
            if (!Uri.TryCreate(page, WebUtility.HtmlDecode(m.Groups[1].Value), out var target)) continue;
            if (target.Scheme != Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeHttp) continue;
            if (!target.AbsolutePath.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (string ext in extensions)
                if (target.AbsolutePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return target;
        }
        return null;
    }

    private static string PageTitle(string html)
    {
        var m = Regex.Match(html, "<meta[^>]+property=\"og:title\"[^>]+content=\"([^\"]*)\"", RegexOptions.IgnoreCase);
        if (!m.Success) m = Regex.Match(html, "<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success) return null;
        string title = Regex.Replace(WebUtility.HtmlDecode(m.Groups[1].Value), @"\s+", " ").Trim();
        title = Regex.Replace(title, @"\s+Cursors?$", "", RegexOptions.IgnoreCase);
        return title.Length > 0 ? title : null;
    }

    private static string PageLicense(string html)
    {
        var m = Regex.Match(html, @"under\s+the\s*<strong>(.*?)</strong>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success) return null;
        string license = Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, "<[^>]+>", "")), @"\s+", " ").Trim();
        return license.Length > 0 ? license : null;
    }

    // The role classes rw-designer puts on each cursor of a set, in Windows registry order.
    private static readonly string[] RoleClasses =
    {
        "curarrow", "curhelp", "curwork", "curbusy", "curprec", "curtext", "curpen", "curunav",
        "curvert", "curhorz", "curnwse", "curnesw", "curmove", "curalt", "curlink",
    };

    /// <summary>File name to role for a set page's cursors; the first cursor tagged with a role is the set's main one.</summary>
    private static Dictionary<string, int> PageRoles(string html)
    {
        var roles = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<int>();
        const string cell = "class=\"itemteaser\\s+([^\"]*)\"><a class=\"download\" href=\"/cursor-download/\\d+/([^\"]+)\"";
        foreach (Match m in Regex.Matches(html, cell, RegexOptions.IgnoreCase))
        {
            int role = m.Groups[1].Value.Split(' ').Select(c => Array.IndexOf(RoleClasses, c.Trim().ToLowerInvariant())).Where(i => i >= 0).DefaultIfEmpty(-1).First();
            string file = Uri.UnescapeDataString(WebUtility.HtmlDecode(m.Groups[2].Value));
            if (role < 0 || roles.ContainsKey(file) || !taken.Add(role)) continue;
            roles[file] = role;
        }
        return roles.Count > 0 ? roles : null;
    }

    private static string SiteName(Uri uri) =>
        uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host.Substring(4) : uri.Host;

    private static string SafeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim(' ', '.');
        return name.Length == 0 ? "download.zip" : name;
    }

    private static HttpWebRequest Request(Uri uri)
    {
        var request = (HttpWebRequest)WebRequest.Create(uri);
        request.UserAgent = UserAgent;
        request.Timeout = 30000;
        request.ReadWriteTimeout = 30000;
        request.AllowAutoRedirect = true;
        request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        return request;
    }

    private static string GetPage(Uri uri)
    {
        using var response = (HttpWebResponse)Request(uri).GetResponse();
        using var stream = response.GetResponseStream();
        using var buffer = new MemoryStream();
        Copy(stream, buffer, MaxPageBytes);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static byte[] GetBytes(Uri uri, long limit)
    {
        using var response = (HttpWebResponse)Request(uri).GetResponse();
        using var stream = response.GetResponseStream();
        using var buffer = new MemoryStream();
        Copy(stream, buffer, limit);
        return buffer.ToArray();
    }

    private static void Download(Uri uri, string path)
    {
        using var response = (HttpWebResponse)Request(uri).GetResponse();
        if (response.ContentLength > MaxDownloadBytes) throw new InvalidDataException("That download is too large for a cursor set");
        using var stream = response.GetResponseStream();
        using var file = File.Create(path);
        Copy(stream, file, MaxDownloadBytes);
    }

    private static void Copy(Stream from, Stream to, long limit)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = from.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > limit) throw new InvalidDataException("That download is too large for a cursor set");
            to.Write(buffer, 0, read);
        }
    }

    private static string Describe(WebException ex)
    {
        if (ex.Response is HttpWebResponse http)
            return http.StatusCode == HttpStatusCode.NotFound ? "That link wasn't found" : $"The site answered {(int)http.StatusCode} {http.StatusDescription}";
        return ex.Status switch
        {
            WebExceptionStatus.Timeout => "The download timed out",
            WebExceptionStatus.NameResolutionFailure or WebExceptionStatus.ConnectFailure => "Couldn't connect. Check your internet connection",
            WebExceptionStatus.TrustFailure or WebExceptionStatus.SecureChannelFailure => "Couldn't make a secure connection to that site",
            _ => "The download failed",
        };
    }
}
