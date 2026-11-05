using Microsoft.Win32;
using PDFile.Core;
using PDFile.Core.Diagnostics;

namespace PDFile.Office;

public static class OfficeConverters
{
    /// <summary>Per-file limit for open+export+close. On expiry the Office process we started is killed.</summary>
    public static TimeSpan FileTimeout { get; set; } = TimeSpan.FromSeconds(90);

    private static OfficeApp[] All() => [new WordApp(), new ExcelApp(), new PowerPointApp()];

    /// <summary>Converters for the Office apps whose ProgID is registered (no app is launched). Each is IDisposable/IAsyncDisposable: dispose on app exit.</summary>
    public static IReadOnlyList<IConverter> Create()
    {
        var result = new List<IConverter>();
        foreach (OfficeApp app in All())
        {
            if (Type.GetTypeFromProgID(app.ProgId) is not null) result.Add(app);
            else app.Dispose();
        }
        return result;
    }

    /// <summary>Multi-line list of detected Office apps and versions for crash reports. Never throws.</summary>
    public static string DescribeInstalled()
    {
        try
        {
            var apps = new (string Name, string ProgId)[] { ("Word", "Word.Application"), ("Excel", "Excel.Application"), ("PowerPoint", "PowerPoint.Application") };
            return OfficeDiagnostics.FormatInstalled(apps.Select(a => Inspect(a.Name, a.ProgId)));
        }
        catch (Exception ex)
        {
            return "Office detection failed: " + ex;
        }
    }

    public static void RegisterWithCrashReport() => CrashReport.ExtraInfoProvider = DescribeInstalled;

    /// <summary>Call after each batch: quits the Office instances PDFile started (blocks up to ~8 s per app). The next file starts a fresh one.</summary>
    public static void EndSessions(IEnumerable<IConverter> converters)
    {
        foreach (OfficeApp app in converters.OfType<OfficeApp>()) app.EndSession();
    }

    private static OfficeDiagnostics.InstalledApp Inspect(string name, string progId)
    {
        bool registered = Type.GetTypeFromProgID(progId) is not null;
        if (!registered) return new(name, progId, false, null, null, null);
        string? curVer = null, exe = null, fileVersion = null;
        try
        {
            // 32-bit Office on 64-bit Windows registers in the 32-bit view; try both.
            foreach (RegistryView view in new[] { RegistryView.Default, RegistryView.Registry32 })
            {
                using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view);
                curVer ??= Read(root, progId + @"\CurVer");
                string? clsid = Read(root, progId + @"\CLSID");
                if (clsid is not null) exe ??= OfficeDiagnostics.ExtractExePath(Read(root, @"CLSID\" + clsid + @"\LocalServer32"));
            }
            if (exe is not null && File.Exists(exe)) fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileVersion;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            Log.Error($"Reading registry details for {progId} failed", ex);
        }
        return new(name, progId, true, curVer, exe, fileVersion);
    }

    private static string? Read(RegistryKey root, string path)
    {
        using RegistryKey? key = root.OpenSubKey(path);
        return key?.GetValue(null) as string;
    }
}
