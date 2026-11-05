using System.Text;

namespace PDFile.Core.Diagnostics;

public static class ExceptionFormatter
{
    public static string Format(Exception ex)
    {
        var sb = new StringBuilder();
        Append(sb, ex, 0);
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, Exception ex, int depth)
    {
        var pad = new string(' ', depth * 2);
        sb.AppendLine($"{pad}{(depth == 0 ? "Exception" : "Caused by")}: {ex.GetType().FullName}: {ex.Message}");
        sb.AppendLine($"{pad}  HRESULT: 0x{ex.HResult:X8}");
        if (ex is ConversionException ce)
        {
            sb.AppendLine($"{pad}  Stage: {ce.Stage}; TimedOut: {ce.TimedOut}");
            if (ce.HResult_ is { } h) sb.AppendLine($"{pad}  Reported HRESULT: 0x{h:X8}");
        }
        if (ex.StackTrace is { } st) sb.AppendLine($"{pad}  Stack trace:{Environment.NewLine}{st}");

        if (ex is AggregateException agg)
            foreach (var inner in agg.Flatten().InnerExceptions) Append(sb, inner, depth + 1);
        else if (ex.InnerException is { } i)
            Append(sb, i, depth + 1);
    }
}
