using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace Cursors;

/// <summary>Reads and writes the per-user cursor scheme (HKCU\Control Panel\Cursors).</summary>
internal static class CursorScheme
{
    private const string KeyPath = @"Control Panel\Cursors";

    /// <summary>
    /// Writes every role to the registry and asks Windows to reload system cursors. Windows reads the same
    /// values at sign-in, so the choice survives restarts without any helper process.
    /// </summary>
    public static void Apply(CursorPack pack, CursorPack fallback)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
        {
            for (int i = 0; i < CursorPack.RoleCount; i++)
            {
                string value = Effective(pack, fallback, i);
                key.SetValue(CursorPack.RegistryNames[i], value,
                    value.IndexOf('%') >= 0 ? RegistryValueKind.ExpandString : RegistryValueKind.String);
            }
            key.SetValue("", pack.SchemeName ?? "", RegistryValueKind.String);
            key.SetValue("Scheme Source", pack.Kind == PackKind.Windows ? 2 : 1, RegistryValueKind.DWord);
        }

        // SPI_SETCURSORS reloads the cursors but reports FALSE with no error code on current Windows builds, so its
        // return value is meaningless. Real failures (e.g. registry access) still surface as exceptions above.
        Native.SystemParametersInfo(Native.SPI_SETCURSORS, 0, IntPtr.Zero, Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
    }

    /// <summary>The value written for a role: the pack's file, else the Windows Default file for that role.</summary>
    public static string Effective(CursorPack pack, CursorPack fallback, int role)
    {
        string own = pack.Values[role];
        if (!string.IsNullOrWhiteSpace(own) && File.Exists(CursorPack.Expand(own))) return own.Trim();
        if (fallback != null && fallback != pack)
        {
            string fb = fallback.Values[role];
            if (!string.IsNullOrWhiteSpace(fb) && File.Exists(CursorPack.Expand(fb))) return fb.Trim();
        }
        return "";
    }

    /// <summary>Current registry values per role; null entries are roles with no value at all.</summary>
    public static string[] ReadCurrent(out string schemeName)
    {
        schemeName = null;
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        if (key == null) return null;
        schemeName = key.GetValue("") as string;
        var values = new string[CursorPack.RoleCount];
        for (int i = 0; i < values.Length; i++)
            values[i] = key.GetValue(CursorPack.RegistryNames[i], null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        return values;
    }

    public static CursorPack FindActive(IEnumerable<CursorPack> packs, CursorPack fallback, string preferredId)
    {
        string[] current = ReadCurrent(out string name);
        if (current == null || current[CursorPack.Arrow] == null) return null;

        var normalized = new string[current.Length];
        for (int i = 0; i < current.Length; i++)
            normalized[i] = current[i] == null ? null : Normalize(current[i]);

        CursorPack preferred = null, byName = null, any = null;
        foreach (var pack in packs)
        {
            if (pack.Kind == PackKind.Catalog || !Matches(pack, fallback, normalized)) continue;
            if (pack.Id == preferredId) preferred = pack;
            if (byName == null && string.Equals(pack.SchemeName, name, StringComparison.OrdinalIgnoreCase)) byName = pack;
            any ??= pack;
        }
        return preferred ?? byName ?? any;
    }

    private static bool Matches(CursorPack pack, CursorPack fallback, string[] normalizedCurrent)
    {
        for (int i = 0; i < CursorPack.RoleCount; i++)
        {
            if (normalizedCurrent[i] == null) continue; // Older Windows builds omit Pin/Person.
            if (normalizedCurrent[i] != Normalize(Effective(pack, fallback, i))) return false;
        }
        return true;
    }

    public static string Normalize(string raw)
    {
        string expanded = CursorPack.Expand(raw);
        if (expanded == null) return "";
        try
        {
            expanded = Path.GetFullPath(expanded);
        }
        catch { }
        return expanded.ToLowerInvariant();
    }
}

/// <summary>Applies schemes off the UI thread. Rapid clicks collapse into a single apply of the latest pack.</summary>
internal sealed class CursorApplier
{
    private readonly object _gate = new();
    private readonly ManualResetEvent _idle = new(true);
    private CursorPack _pending, _pendingFallback;
    private bool _running;

    public event Action<CursorPack, Exception> Completed;

    public void Request(CursorPack pack, CursorPack fallback)
    {
        lock (_gate)
        {
            _pending = pack;
            _pendingFallback = fallback;
            if (_running) return;
            _running = true;
            _idle.Reset();
        }
        ThreadPool.QueueUserWorkItem(_ => Run());
    }

    public bool WaitIdle(int timeoutMs) => _idle.WaitOne(timeoutMs);

    private void Run()
    {
        while (true)
        {
            CursorPack pack, fallback;
            lock (_gate)
            {
                pack = _pending;
                fallback = _pendingFallback;
                _pending = null;
                if (pack == null)
                {
                    _running = false;
                    _idle.Set();
                    return;
                }
            }

            Exception error = null;
            try
            {
                PackLibrary.Install(pack);
                CursorScheme.Apply(pack, fallback);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            Completed?.Invoke(pack, error);
        }
    }
}
