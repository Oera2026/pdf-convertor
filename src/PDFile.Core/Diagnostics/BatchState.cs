namespace PDFile.Core.Diagnostics;

public sealed record BatchSnapshot(string CurrentFile, string ConverterName, string Stage, int Done, int Total, OutputMode? OutputMode, string OutputPath)
{
    public override string ToString() =>
        $"File={CurrentFile}; Converter={ConverterName}; Stage={Stage}; Done={Done}/{Total}; Mode={OutputMode?.ToString() ?? "-"}; Output={OutputPath}";
}

public static class BatchState
{
    private static readonly object Gate = new();
    private static BatchSnapshot _current = new("", "", "idle", 0, 0, null, "");

    public static BatchSnapshot Snapshot { get { lock (Gate) return _current; } }

    public static void Update(Func<BatchSnapshot, BatchSnapshot> change)
    {
        lock (Gate) _current = change(_current);
    }

    public static void SetStage(string stage) => Update(s => s with { Stage = stage });
}
