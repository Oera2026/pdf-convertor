#pragma warning disable S6966, VSTHRD103 // sync IO is fine in tests
using Xunit;
using System.Runtime.CompilerServices;
using PDFile.Core;
using PDFile.Core.Diagnostics;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PDFile.Tests;

internal static class CoreTestSetup
{
    [ModuleInitializer]
    internal static void Init() =>
        Environment.SetEnvironmentVariable("PDFILE_DATA", Path.Combine(Path.GetTempPath(), "PDFileTestData-" + Guid.NewGuid().ToString("N")));
}

public sealed class CoreTempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pdfile-test-" + Guid.NewGuid().ToString("N"));
    public CoreTempDir() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, true);
}

public class CoreScannerTests
{
    [Fact]
    public void NaturalSortAndSkipRules()
    {
        using var d = new CoreTempDir();
        Directory.CreateDirectory(Path.Combine(d.Path, "sub"));
        foreach (var f in new[] { "10.docx", "2.docx", "B.DOCX", "a.docx", "~$lock.docx", "x.xyz", "sub/1.docx" })
            File.WriteAllText(Path.Combine(d.Path, f), "x");
        File.WriteAllText(Path.Combine(d.Path, ".hid.docx"), "x"); // dot-files count as hidden on Unix
        if (OperatingSystem.IsWindows()) File.SetAttributes(Path.Combine(d.Path, ".hid.docx"), FileAttributes.Hidden);

        var flat = FolderScanner.Scan(d.Path, false, e => e == ".docx");
        Assert.Equal(new[] { "2.docx", "10.docx", "a.docx", "B.DOCX" }, flat.Jobs.Select(j => j.RelativePath));
        Assert.Equal(3, flat.SkippedCount); // ~$ lock, .xyz, hidden

        var deep = FolderScanner.Scan(d.Path, true, e => e == ".docx");
        Assert.Contains(deep.Jobs, j => j.RelativePath == Path.Combine("sub", "1.docx"));
    }
}

[Collection("core-state")] // BatchState is global
public class CoreBatchRunnerTests
{
    private sealed class FakeConverter : IConverter
    {
        public Func<string, CancellationToken, Task>? Hook;
        public bool CanConvert(string extension) => extension == ".txt";
        public async Task ConvertAsync(string sourcePath, string destPdfPath, CancellationToken ct)
        {
            if (Hook is not null) await Hook(sourcePath, ct);
            File.WriteAllText(destPdfPath, "pdf");
        }
    }

    private sealed class FakeMerger : IPdfMerger
    {
        public IReadOnlyList<MergePart>? Parts;
        public Task MergeAsync(IReadOnlyList<MergePart> parts, string outputPath, CancellationToken ct)
        {
            Parts = parts;
            File.WriteAllText(outputPath, "merged");
            return Task.CompletedTask;
        }
    }

    private static ConversionJob Job(CoreTempDir d, string rel)
    {
        var p = Path.Combine(d.Path, "in", rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "x");
        return new ConversionJob(p, rel);
    }

    [Fact]
    public async Task SeparateModeFailureContinuesAndCollisionsAreNumbered()
    {
        using var d = new CoreTempDir();
        var conv = new FakeConverter { Hook = (s, _) => s.EndsWith("bad.txt") ? throw new ConversionException("export", "boom", null, unchecked((int)0x80004005)) : Task.CompletedTask };
        var jobs = new[] { Job(d, "a.txt"), Job(d, "bad.txt"), Job(d, Path.Combine("s", "c.txt")) };
        var opts = new BatchOptions(jobs, OutputMode.SeparatePdfs, Path.Combine(d.Path, "out"));
        var runner = new BatchRunner([conv], new FakeMerger());

        var sum = await runner.RunAsync(opts, new Progress<BatchProgress>(), default);
        Assert.Equal(new[] { Outcome.Succeeded, Outcome.Failed, Outcome.Succeeded }, sum.Results.Select(r => r.Outcome));
        Assert.Contains("0x80004005", sum.Results[1].ErrorReport);
        Assert.Contains("Stage: export", sum.Results[1].ErrorReport);
        Assert.True(File.Exists(Path.Combine(d.Path, "out", "s", "c.pdf")));

        var again = await runner.RunAsync(opts with { Jobs = [jobs[0]] }, new Progress<BatchProgress>(), default);
        Assert.EndsWith("a (2).pdf", again.Results[0].PdfPath);
    }

    [Fact]
    public async Task MergedModeUsesPdfInPlaceAndCleansTemp()
    {
        using var d = new CoreTempDir();
        var merger = new FakeMerger();
        var pdf = Job(d, "p.pdf");
        var jobs = new[] { Job(d, "a.txt"), pdf };
        var outPath = Path.Combine(d.Path, "all.pdf");
        var sum = await new BatchRunner([new FakeConverter()], merger).RunAsync(new BatchOptions(jobs, OutputMode.MergedPdf, outPath), new Progress<BatchProgress>(), default);

        Assert.Equal(outPath, sum.MergedPdfPath);
        Assert.Equal(new[] { "a", "p" }, merger.Parts!.Select(p => p.Bookmark));
        Assert.Equal(pdf.SourcePath, merger.Parts![1].PdfPath);
        Assert.False(File.Exists(merger.Parts[0].PdfPath)); // temp deleted
    }

    [Fact]
    public async Task CancellationMarksRemainingAndSkipsMerge()
    {
        using var d = new CoreTempDir();
        using var cts = new CancellationTokenSource();
        var conv = new FakeConverter { Hook = (_, _) => { cts.Cancel(); return Task.CompletedTask; } };
        var jobs = new[] { Job(d, "a.txt"), Job(d, "b.txt") };
        var outPath = Path.Combine(d.Path, "all.pdf");
        var sum = await new BatchRunner([conv], new FakeMerger()).RunAsync(new BatchOptions(jobs, OutputMode.MergedPdf, outPath), new Progress<BatchProgress>(), cts.Token);

        Assert.Null(sum.MergedPdfPath);
        Assert.False(File.Exists(outPath));
        Assert.Equal(Outcome.Cancelled, sum.Results[1].Outcome);
    }
}

[Collection("core-state")]
public class CoreDiagnosticsTests
{
    [Fact]
    public void FormatterShowsNestedAggregateAndHresult()
    {
        var inner = new ConversionException("open", "inner", null, unchecked((int)0x800706BA)) { TimedOut = true };
        var ex = new AggregateException(new InvalidOperationException("outer", inner), new IOException("io"));
        var text = ExceptionFormatter.Format(ex);
        Assert.Contains("Stage: open; TimedOut: True", text);
        Assert.Contains("0x800706BA", text);
        Assert.Contains("System.IO.IOException", text);
        Assert.Contains("outer", text);
    }

    [Fact]
    public void CrashReportIsWrittenWithBatchState()
    {
        BatchState.Update(_ => new BatchSnapshot("c:\\x\\crashy.docx", "WordConverter", "export", 1, 5, OutputMode.MergedPdf, "o.pdf"));
        CrashReport.ExtraInfoProvider = () => "Office 16";
        Log.Info("hello log");
        var path = CrashReport.WriteToDisk(new InvalidOperationException("kaboom"), "test");
        var text = File.ReadAllText(path);
        Assert.Contains("crashy.docx", text);
        Assert.Contains("kaboom", text);
        Assert.Contains("Office 16", text);
        Assert.Contains("hello log", text);
    }
}

public class CoreMergerTests
{
    [Fact]
    public async Task MergesThreePdfsWithBookmarks()
    {
        using var d = new CoreTempDir();
        var parts = new List<MergePart>();
        for (var i = 1; i <= 3; i++)
        {
            var p = Path.Combine(d.Path, $"{i}.pdf");
            using (var doc = new PdfDocument())
            {
                doc.AddPage();
                if (i == 2) doc.AddPage();
                doc.Save(p);
            }
            parts.Add(new MergePart(p, $"part{i}"));
        }
        var outPath = Path.Combine(d.Path, "m.pdf");
        await new PdfMerger().MergeAsync(parts, outPath, default);

        using var merged = PdfReader.Open(outPath, PdfDocumentOpenMode.Import);
        Assert.Equal(4, merged.PageCount);
        Assert.Equal(3, merged.Outlines.Count);
    }

    [Fact]
    public async Task CorruptPartNamesFileInConversionException()
    {
        using var d = new CoreTempDir();
        var bad = Path.Combine(d.Path, "bad.pdf");
        File.WriteAllText(bad, "not a pdf");
        var ex = await Assert.ThrowsAsync<ConversionException>(() => new PdfMerger().MergeAsync([new MergePart(bad, "bad")], Path.Combine(d.Path, "o.pdf"), default));
        Assert.Equal("merge", ex.Stage);
        Assert.Contains("bad.pdf", ex.Message);
    }
}
