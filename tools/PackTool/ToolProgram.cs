using System;
using System.IO;
using System.Linq;

namespace Cursors.PackTool;

/// <summary>
/// Builds and verifies the bundled cursor library.
///   build     &lt;recipe&gt; &lt;sources root&gt; &lt;packs out&gt; [ids...]
///   verify    &lt;packs dir&gt; &lt;report dir&gt;
///   applytest &lt;packs dir&gt; [ids...]
///   inspect   &lt;file&gt;
/// </summary>
internal static class ToolProgram
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Upstream archives nest deeply (Hackneyed's zip repeats its folder name), so lift .NET Framework's legacy
        // 248/260-character checks before any file I/O and address the sources through \\?\ paths.
        AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
        AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);

        if (args.Length == 0)
        {
            Console.WriteLine("usage: PackTool build|verify|applytest|inspect ...");
            return 2;
        }
        try
        {
            switch (args[0])
            {
                case "build":
                    return PackBuilder.BuildAll(args[1], ExtendedPath(args[2]), args[3], args.Skip(4).ToArray());
                case "verify":
                    return PackVerifier.Verify(args[1], args[2]);
                case "applytest":
                    return PackVerifier.ApplyTest(args[1], args.Skip(2).ToArray());
                case "inspect":
                    Inspect(args[1]);
                    return 0;
                default:
                    Console.WriteLine("unknown command " + args[0]);
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 99;
        }
    }

    /// <summary>Local paths become \\?\ paths, which Windows doesn't limit to MAX_PATH (UNC paths are left alone).</summary>
    private static string ExtendedPath(string path)
    {
        string full = Path.GetFullPath(path).TrimEnd('\\');
        return full.StartsWith(@"\\", StringComparison.Ordinal) ? full : @"\\?\" + full;
    }

    private static void Inspect(string path)
    {
        if (XCursorReader.IsXCursor(path))
        {
            foreach (var kv in XCursorReader.Read(path))
                Console.WriteLine($"xcursor nominal {kv.Key}: {kv.Value.Count} image(s), {kv.Value[0].Image.Width}x{kv.Value[0].Image.Height} hot {kv.Value[0].Image.HotX},{kv.Value[0].Image.HotY} delay {kv.Value[0].Delay}");
            return;
        }
        byte[] data = File.ReadAllBytes(path);
        var frames = WindowsCursorFiles.ReadFrames(data);
        Console.WriteLine($"{(WindowsCursorFiles.IsAni(data) ? "ani" : "cur")} frames={frames.Count}");
        foreach (var e in frames[0])
            Console.WriteLine($"  {e.Width}x{e.Height} hot {e.HotX},{e.HotY} {(e.IsPng ? "png" : "bmp")} {e.Data.Length} bytes");
    }
}
