using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PDFile.Core;

public sealed class PdfMerger : IPdfMerger
{
    // shortcut: PDFsharp import copies pages into the output document in memory; sources are opened one at a time, so peak = output size. Upgrade if multi-GB merges appear.
    public Task MergeAsync(IReadOnlyList<MergePart> parts, string outputPath, CancellationToken ct) =>
        Task.Run(() =>
        {
            using var output = new PdfDocument();
            foreach (var part in parts)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var src = PdfReader.Open(part.PdfPath, PdfDocumentOpenMode.Import);
                    if (src.PageCount == 0) throw new InvalidOperationException("PDF has no pages.");
                    var first = output.AddPage(src.Pages[0]);
                    for (var i = 1; i < src.PageCount; i++) output.AddPage(src.Pages[i]);
                    output.Outlines.Add(part.Bookmark, first, true);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    throw new ConversionException("merge", $"Cannot merge '{part.PdfPath}' ({e.Message})", e);
                }
            }
            ct.ThrowIfCancellationRequested();
            output.Save(outputPath);
        }, ct);
}
