using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Cursors;

/// <summary>
/// Start menu and desktop shortcuts, so the portable app shows up in Windows search. The Start menu shortcut is kept
/// in place; the desktop one is created on first run only, so deleting it sticks. Both follow the exe if its folder moves.
/// </summary>
internal static class Shortcuts
{
    private const string ShortcutName = "Cursors.lnk";

    public static string StartMenuPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);
    public static string DesktopPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName);

    public static void Ensure()
    {
        try
        {
            string exe = Application.ExecutablePath;
            var settings = IniFile.Read(AppPaths.SettingsFile);
            bool firstRun = !settings.ContainsKey("shortcuts");
            Update(StartMenuPath, exe, createIfMissing: true);
            Update(DesktopPath, exe, createIfMissing: firstRun);
            if (firstRun)
            {
                settings["shortcuts"] = "1";
                IniFile.Write(AppPaths.SettingsFile, settings);
            }
        }
        catch { }
    }

    /// <summary>Deletes the shortcuts that point at this app.</summary>
    public static void Remove()
    {
        foreach (string path in new[] { StartMenuPath, DesktopPath })
        {
            try
            {
                if (File.Exists(path) && IsOurs(Target(path))) File.Delete(path);
            }
            catch { }
        }
    }

    private static void Update(string path, string exe, bool createIfMissing)
    {
        if (File.Exists(path))
        {
            // Only a shortcut to a Cursors.exe is ours to fix, for example after the app's folder was moved.
            string target = Target(path);
            if (!IsOurs(target) || string.Equals(Path.GetFullPath(target), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase)) return;
        }
        else if (!createIfMissing)
        {
            return;
        }
        Create(path, exe);
    }

    private static bool IsOurs(string target) =>
        target != null && Path.GetFileName(target).Equals(Path.GetFileName(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase);

    private static string Target(string shortcut)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Load(shortcut, 0);
            var path = new StringBuilder(260);
            link.GetPath(path, path.Capacity, IntPtr.Zero, 0);
            return path.Length > 0 ? path.ToString() : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    private static void Create(string shortcut, string exe)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(exe);
            link.SetWorkingDirectory(Path.GetDirectoryName(exe));
            link.SetDescription("One-click cursor packs for Windows");
            link.SetIconLocation(exe, 0);
            Directory.CreateDirectory(Path.GetDirectoryName(shortcut));
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(shortcut, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxIconPath, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
