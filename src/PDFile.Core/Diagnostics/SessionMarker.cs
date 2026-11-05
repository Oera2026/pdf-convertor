namespace PDFile.Core.Diagnostics;

/// <summary>A marker file exists while the app runs; finding it at start means the last session did not exit cleanly.</summary>
public static class SessionMarker
{
    private static readonly object Gate = new();
    private static bool _loaded;
    private static bool _abnormal;
    private static string? _detail;
    private static DateTime _lastSave = DateTime.MinValue;

    private static string MarkerPath => Path.Combine(AppPaths.Root, "session.marker");

    public static bool PreviousSessionEndedAbnormally(out string? detail)
    {
        lock (Gate)
        {
            Load();
            detail = _detail;
            return _abnormal;
        }
    }

    public static void Begin()
    {
        lock (Gate)
        {
            Load();
            Save("started", force: true);
        }
    }

    public static void End()
    {
        lock (Gate)
        {
            try { File.Delete(MarkerPath); }
            catch (Exception e) { Log.Warn("Could not delete session marker: " + e.Message); }
        }
    }

    /// <summary>Stores the current BatchState in the marker, at most every 2 seconds.</summary>
    public static void SaveState()
    {
        lock (Gate) Save(BatchState.Snapshot.ToString(), force: false);
    }

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(MarkerPath)) return;
            _detail = File.ReadAllText(MarkerPath);
            _abnormal = !OwnerIsRunning(_detail);
        }
        catch (Exception e)
        {
            Log.Warn("Could not read session marker: " + e.Message);
        }
    }

    /// <summary>True when the marker belongs to another PDFile process that is still open (a second window, not a crash).</summary>
    internal static bool OwnerIsRunning(string marker)
    {
        var m = System.Text.RegularExpressions.Regex.Match(marker, @" pid (\d+):", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out var pid) || pid == Environment.ProcessId) return false;
        try
        {
            using var owner = System.Diagnostics.Process.GetProcessById(pid);
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            return owner.ProcessName == self.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) // not running (any more)
        {
            return false;
        }
    }

    private static void Save(string content, bool force)
    {
        if (!force && DateTime.UtcNow - _lastSave < TimeSpan.FromSeconds(2)) return;
        _lastSave = DateTime.UtcNow;
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(MarkerPath, $"{DateTime.Now:O} pid {Environment.ProcessId}: {content}");
        }
        catch (Exception e)
        {
            Log.Warn("Could not write session marker: " + e.Message);
        }
    }
}
