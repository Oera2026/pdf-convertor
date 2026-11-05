using System.Diagnostics;
using PDFile.Core.Diagnostics;
using PdfSharp.Pdf.IO;

namespace PDFile.Core;

public enum OutputMode { SeparatePdfs, MergedPdf }

/// <param name="OutputPath">Folder for SeparatePdfs, full .pdf file path for MergedPdf.</param>
public sealed record BatchOptions(IReadOnlyList<ConversionJob> Jobs, OutputMode Mode, string OutputPath);
/// <param name="Job">The file this report is about; null for the merge step. File names alone are not unique.</param>
public sealed record BatchProgress(int Done, int Total, string CurrentFile, string Stage, ConversionJob? Job = null);
public sealed record BatchSummary(IReadOnlyList<ConversionResult> Results, string? MergedPdfPath, TimeSpan Elapsed);

public sealed class BatchRunner(IEnumerable<IConverter> converters, IPdfMerger merger)
{
    private readonly List<IConverter> _converters = converters.ToList();

    public async Task<BatchSummary> RunAsync(BatchOptions options, IProgress<BatchProgress> progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var merged = options.Mode == OutputMode.MergedPdf;
        var results = new List<ConversionResult>();
        var parts = new List<(int Index, MergePart Part)>();
        var total = options.Jobs.Count;
        var tempRoot = Path.Combine(Path.GetTempPath(), "PDFile");
        var tempDir = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
        string? mergedPath = null;

        BatchState.Update(_ => new BatchSnapshot("", "", "starting", 0, total, options.Mode, options.OutputPath));
        SessionMarker.SaveState();
        Log.Info($"Batch start: {total} files, {options.Mode}, output {options.OutputPath}");
        SweepStaleTempDirs(tempRoot);
        try
        {
            Directory.CreateDirectory(tempDir);
            for (var i = 0; i < total; i++)
            {
                var job = options.Jobs[i];
                if (ct.IsCancellationRequested)
                {
                    results.Add(new ConversionResult(job, Outcome.Cancelled, null, null, TimeSpan.Zero));
                    continue;
                }
                var result = await ConvertOneAsync(job, i, options, tempDir, total, progress, ct);
                results.Add(result);
                if (merged && result.Outcome == Outcome.Succeeded) parts.Add((i, new MergePart(result.PdfPath!, BookmarkFor(job, options.Jobs))));
                progress.Report(new BatchProgress(i + 1, total, job.RelativePath, result.Outcome.ToString(), job));
            }

            if (merged && parts.Count > 0 && !ct.IsCancellationRequested)
                mergedPath = await MergeAsync(options, tempDir, parts, results, total, progress, ct);
            else if (merged && ct.IsCancellationRequested) // no merged file was written, so nothing counts as converted
                for (var i = 0; i < results.Count; i++)
                    if (results[i].Outcome == Outcome.Succeeded) results[i] = results[i] with { Outcome = Outcome.Cancelled };
        }
        finally
        {
            BatchState.Update(s => s with { Stage = "idle", CurrentFile = "" });
            DeleteDir(tempDir);
        }
        if (merged) ClearPdfPaths(results); // temp PDFs are gone; the merged file is the product
        Log.Info($"Batch end: {results.Count(r => r.Outcome == Outcome.Succeeded)} ok, {results.Count(r => r.Outcome == Outcome.Failed)} failed, {results.Count(r => r.Outcome == Outcome.Cancelled)} cancelled");
        return new BatchSummary(results, mergedPath, sw.Elapsed);
    }

    private async Task<ConversionResult> ConvertOneAsync(ConversionJob job, int index, BatchOptions options, string tempDir, int total, IProgress<BatchProgress> progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var ext = Path.GetExtension(job.SourcePath).ToLowerInvariant();
        var converter = _converters.FirstOrDefault(c => c.CanConvert(ext));
        var stage = "convert";
        // Always convert into the short temp path: Office has path-length limits, and the output folder never holds a half-written PDF.
        var tmp = Path.Combine(tempDir, $"{index}.pdf");
        BatchState.Update(_ => new BatchSnapshot(job.SourcePath, converter?.GetType().Name ?? "", "convert", index, total, options.Mode, options.OutputPath));
        SessionMarker.SaveState();
        progress.Report(new BatchProgress(index, total, job.RelativePath, "Converting", job));
        try
        {
            if (options.Mode == OutputMode.MergedPdf && ext == ".pdf")
            {
                stage = "read";
                CheckReadablePdf(job.SourcePath); // one unreadable PDF fails alone instead of failing the whole merge
                return new ConversionResult(job, Outcome.Succeeded, job.SourcePath, null, sw.Elapsed); // used in place
            }
            if (converter is null)
            {
                if (ext != ".pdf") throw new ConversionException("select", $"No converter for '{ext}' files.");
                File.Copy(job.SourcePath, tmp);
            }
            else
            {
                await converter.ConvertAsync(job.SourcePath, tmp, ct);
            }
            if (options.Mode == OutputMode.MergedPdf)
                return new ConversionResult(job, Outcome.Succeeded, tmp, null, sw.Elapsed);
            stage = "write";
            var dest = UniquePath(Path.Combine(options.OutputPath, Path.ChangeExtension(job.RelativePath, ".pdf")));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Move(tmp, dest); // no overwrite: if the name was taken meanwhile this fails rather than replacing a file
            return new ConversionResult(job, Outcome.Succeeded, dest, null, sw.Elapsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            DeleteFile(tmp);
            return new ConversionResult(job, Outcome.Cancelled, null, null, sw.Elapsed);
        }
        catch (Exception ex)
        {
            DeleteFile(tmp);
            if (ex is ConversionException ce) stage = ce.Stage;
            var report = BuildReport(stage, job.SourcePath, converter, ex);
            Log.Error($"Conversion failed: {job.SourcePath}", ex);
            return new ConversionResult(job, Outcome.Failed, null, report, sw.Elapsed);
        }
    }

    private async Task<string?> MergeAsync(BatchOptions options, string tempDir, List<(int Index, MergePart Part)> parts, List<ConversionResult> results, int total, IProgress<BatchProgress> progress, CancellationToken ct)
    {
        var output = UniquePath(options.OutputPath);
        var tmp = Path.Combine(tempDir, "merged.pdf"); // deleted with tempDir
        BatchState.Update(s => s with { Stage = "merge", CurrentFile = output, ConverterName = merger.GetType().Name });
        progress.Report(new BatchProgress(total, total, output, "Merging"));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await merger.MergeAsync(parts.Select(p => p.Part).ToList(), tmp, ct);
            File.Move(tmp, output); // the merged file appears complete or not at all
            return output;
        }
        catch (Exception ex)
        {
            var cancelled = ex is OperationCanceledException && ct.IsCancellationRequested;
            var report = cancelled ? null : BuildReport("merge", output, null, ex, merger.GetType().Name);
            if (!cancelled) Log.Error("Merge failed: " + output, ex);
            foreach (var (index, _) in parts)
                results[index] = results[index] with { Outcome = cancelled ? Outcome.Cancelled : Outcome.Failed, ErrorReport = report };
            return null;
        }
    }

    private static string BuildReport(string stage, string path, IConverter? converter, Exception ex, string? componentName = null)
    {
        long size = -1;
        if (File.Exists(path)) size = new FileInfo(path).Length;
        return $"Stage: {stage}{Environment.NewLine}File: {path}{Environment.NewLine}Size: {size} bytes{Environment.NewLine}" +
               $"Converter: {componentName ?? converter?.GetType().FullName ?? "(none)"}{Environment.NewLine}{ExceptionFormatter.Format(ex)}";
    }

    private static void CheckReadablePdf(string path)
    {
        using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        if (pdf.PageCount == 0) throw new InvalidDataException("PDF has no pages.");
    }

    private static void ClearPdfPaths(List<ConversionResult> results)
    {
        for (var i = 0; i < results.Count; i++) results[i] = results[i] with { PdfPath = null };
    }

    private static string BookmarkFor(ConversionJob job, IReadOnlyList<ConversionJob> all)
    {
        var name = Path.GetFileNameWithoutExtension(job.RelativePath);
        var clash = all.Count(j => string.Equals(Path.GetFileNameWithoutExtension(j.RelativePath), name, StringComparison.OrdinalIgnoreCase)) > 1;
        return clash ? job.RelativePath : name;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? "";
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var n = 2;
        string candidate;
        do candidate = Path.Combine(dir, $"{stem} ({n++}){ext}");
        while (File.Exists(candidate));
        return candidate;
    }

    private static void SweepStaleTempDirs(string tempRoot)
    {
        if (!Directory.Exists(tempRoot)) return;
        foreach (var d in Directory.EnumerateDirectories(tempRoot).Where(d => Directory.GetLastWriteTimeUtc(d) < DateTime.UtcNow.AddDays(-1)))
            DeleteDir(d);
    }

    private static void DeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Warn($"Could not delete temp dir {dir}: {e.Message}"); }
    }

    private static void DeleteFile(string? path)
    {
        try { if (path is not null) File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Warn($"Could not delete partial file {path}: {e.Message}"); }
    }
}
