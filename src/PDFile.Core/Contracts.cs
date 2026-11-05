namespace PDFile.Core;

/// <summary>Converts one source file to one PDF. Throws <see cref="ConversionException"/> on failure; never swallows.</summary>
public interface IConverter
{
    /// <summary>Lower-case extension including the dot, e.g. ".docx".</summary>
    bool CanConvert(string extension);

    /// <summary>Writes a PDF to <paramref name="destPdfPath"/> (parent dir already exists). Must honour <paramref name="ct"/>.</summary>
    Task ConvertAsync(string sourcePath, string destPdfPath, CancellationToken ct);
}

/// <summary>Merges PDFs in order by streaming pages from disk; one bookmark per part.</summary>
public interface IPdfMerger
{
    Task MergeAsync(IReadOnlyList<MergePart> parts, string outputPath, CancellationToken ct);
}

public sealed record MergePart(string PdfPath, string Bookmark);

/// <summary>A file chosen for conversion. RelativePath is relative to the scanned root (used to mirror folders).</summary>
public sealed record ConversionJob(string SourcePath, string RelativePath);

public enum Outcome { Succeeded, Failed, Cancelled }

public sealed record ConversionResult(ConversionJob Job, Outcome Outcome, string? PdfPath, string? ErrorReport, TimeSpan Elapsed);

/// <summary>Failure with full technical context. Converters add Stage ("open", "export", "close", "merge"...) and HRESULT when known.</summary>
public sealed class ConversionException : Exception
{
    public string Stage { get; }
    public int? HResult_ { get; }
    public bool TimedOut { get; init; }
    public ConversionException(string stage, string message, Exception? inner = null, int? hresult = null)
        : base(message, inner) { Stage = stage; HResult_ = hresult; }
}
