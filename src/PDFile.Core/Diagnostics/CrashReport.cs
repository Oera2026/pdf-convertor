using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace PDFile.Core.Diagnostics;

public static class CrashReport
{
    /// <summary>The Office project plugs in installed Office versions etc.</summary>
    public static Func<string>? ExtraInfoProvider { get; set; }

    public static string Build(Exception ex, string origin)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PDF Convertor crash report {DateTime.Now:O}");
        sb.AppendLine($"Origin: {origin}");
        sb.AppendLine($"App version: {Assembly.GetEntryAssembly()?.GetName().Version ?? typeof(CrashReport).Assembly.GetName().Version}");
        sb.AppendLine($".NET: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}); process {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"Memory: working set {Environment.WorkingSet / 1048576} MB, GC heap {GC.GetTotalMemory(false) / 1048576} MB");
        sb.AppendLine($"Thread: {Environment.CurrentManagedThreadId} ({Thread.CurrentThread.Name ?? "unnamed"})");
        sb.AppendLine($"Command line: {Environment.CommandLine}");
        sb.AppendLine($"Batch state: {BatchState.Snapshot}");
        sb.AppendLine($"Log file failure: {Log.FileFailure ?? "none"}");
        sb.AppendLine().AppendLine("--- Extra info ---").AppendLine(Extra());
        sb.AppendLine("--- Exception ---").AppendLine(ExceptionFormatter.Format(ex));
        sb.AppendLine("--- Last log lines ---");
        foreach (var l in Log.Recent(200)) sb.AppendLine(l);
        return sb.ToString();
    }

    public static string WriteToDisk(Exception ex, string origin)
    {
        var path = "";
        try
        {
            var dir = Path.Combine(AppPaths.Root, "crashes");
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            if (File.Exists(path)) path = Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.txt");
            File.WriteAllText(path, Build(ex, origin));
            Log.Error($"Crash report written to {path} (origin {origin})", ex);
        }
        catch (Exception e)
        {
            Log.Error($"Could not write crash report ({origin}): {e.Message}", e);
        }
        return path;
    }

    private static string Extra()
    {
        try { return ExtraInfoProvider?.Invoke() ?? "(none)"; }
        catch (Exception e) { return "ExtraInfoProvider failed: " + e.Message; }
    }
}
