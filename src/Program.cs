using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace Cursors;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && (args[0].Equals("--restore", StringComparison.OrdinalIgnoreCase) || args[0].Equals("/restore", StringComparison.OrdinalIgnoreCase)))
        {
            // Headless escape hatch: Cursors.exe --restore puts the Windows Default pointers back.
            try
            {
                var library = PackLibrary.Load();
                CursorScheme.Apply(library.WindowsDefault, library.WindowsDefault);
                return 0;
            }
            catch (Exception)
            {
                return 1;
            }
        }

        if (args.Length > 0 && (args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase) || args[0].Equals("/uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            // Cursors.exe --uninstall puts the Windows Default pointers back and deletes the app's shortcuts.
            Shortcuts.Remove();
            try
            {
                var library = PackLibrary.Load();
                CursorScheme.Apply(library.WindowsDefault, library.WindowsDefault);
                return 0;
            }
            catch (Exception)
            {
                return 1;
            }
        }

        using var mutex = new Mutex(true, @"Local\Cursors.App.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            ActivateExisting();
            return 0;
        }

        Shortcuts.Ensure();
        Native.EnableDarkMenus();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainWindow());
        return 0;
    }

    private static void ActivateExisting()
    {
        var me = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(me.ProcessName))
        {
            if (process.Id == me.Id) continue;
            IntPtr hwnd = process.MainWindowHandle;
            if (hwnd == IntPtr.Zero) continue;
            if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, 9); // SW_RESTORE
            Native.SetForegroundWindow(hwnd);
            break;
        }
    }
}
