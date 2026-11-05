namespace PDFile.Core.Diagnostics;

public static class Log
{
    private const int RingSize = 200;
    private static readonly object Gate = new();
    private static readonly Queue<string> Ring = new();
    private static bool _pruned;

    public static string LogDirectory => Path.Combine(AppPaths.Root, "logs");

    /// <summary>Set when the log file could not be written (ring buffer only).</summary>
    public static string? FileFailure { get; private set; }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : message + Environment.NewLine + ExceptionFormatter.Format(ex));

    public static IReadOnlyList<string> Recent(int n = RingSize)
    {
        lock (Gate) return Ring.TakeLast(Math.Max(0, n)).ToList();
    }

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [T{Environment.CurrentManagedThreadId}] {message}";
        lock (Gate)
        {
            Ring.Enqueue(line);
            while (Ring.Count > RingSize) Ring.Dequeue();
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(Path.Combine(LogDirectory, $"pdfile-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine);
                Prune();
            }
            catch (Exception e)
            {
                FileFailure = $"{e.GetType().Name}: {e.Message}"; // logging must never throw
            }
        }
    }

    // Keep two weeks of daily logs; runs once per process.
    private static void Prune()
    {
        if (_pruned) return;
        _pruned = true;
        foreach (var f in Directory.EnumerateFiles(LogDirectory, "pdfile-*.log").Where(f => File.GetLastWriteTime(f) < DateTime.Now.AddDays(-14)))
            File.Delete(f);
    }
}
