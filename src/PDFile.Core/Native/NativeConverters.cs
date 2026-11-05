using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace PDFile.Core.Native;

/// <summary>All converters that need no Office and no external program.</summary>
public static class NativeConverters
{
    public static IReadOnlyList<IConverter> All { get; } =
    [
        new ImageConverter(), new TextConverter(), new MarkdownConverter(), new HtmlConverter(), new PdfPassThroughConverter(),
    ];
}

/// <summary>Runs Render into dest + ".tmp" on a worker thread, wraps failures, then renames atomically.</summary>
internal abstract class NativeConverter : IConverter
{
    protected abstract string[] Extensions { get; }
    protected abstract void Render(string source, string tmp, CancellationToken ct);

    public bool CanConvert(string extension) => Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    public Task ConvertAsync(string sourcePath, string destPdfPath, CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        var tmp = destPdfPath + ".tmp";
        try
        {
            try { Render(sourcePath, tmp, ct); }
            catch (Exception e) when (e is not (OperationCanceledException or ConversionException))
            {
                var stage = e is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException ? "read" : "render";
                throw new ConversionException(stage, $"{sourcePath}: {e.Message}", e);
            }
            ct.ThrowIfCancellationRequested();
            try { File.Move(tmp, destPdfPath, true); }
            catch (IOException e) { throw new ConversionException("write", $"{sourcePath}: cannot write {destPdfPath}: {e.Message}", e); }
        }
        finally { File.Delete(tmp); } // no-op after a successful move
    }, ct);
}

internal sealed class PdfPassThroughConverter : NativeConverter
{
    protected override string[] Extensions => [".pdf"];
    protected override void Render(string source, string tmp, CancellationToken ct) => File.Copy(source, tmp, true);
}

internal sealed class ImageConverter : NativeConverter
{
    // shortcut: multi-frame TIFF/GIF yields only the first frame; upgrade if page-per-frame is needed.
    protected override string[] Extensions => [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"];

    // PDFsharp's core build only decodes PNG/JPEG; BMP/GIF/TIFF are re-encoded to PNG with System.Drawing (Windows only).
    // PDF ignores EXIF orientation, so sideways phone photos (JPEG) are rotated with System.Drawing too.
    private static XImage Load(string source, MemoryStream buffer)
    {
        var ext = Path.GetExtension(source).ToLowerInvariant();
        if (ext == ".png") return XImage.FromFile(source);
        if (ext is ".jpg" or ".jpeg")
        {
            if (!OperatingSystem.IsWindows() || !TryRotateUpright(source, buffer)) return XImage.FromFile(source);
        }
        else if (OperatingSystem.IsWindows()) ToPng(source, buffer);
        else throw new PlatformNotSupportedException("BMP/GIF/TIFF conversion needs Windows");
        buffer.Position = 0;
        return XImage.FromStream(buffer);
    }

    /// <summary>Writes the JPEG re-encoded with its EXIF orientation applied; false (nothing written) if it is already upright.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool TryRotateUpright(string source, Stream jpeg)
    {
        const int orientationTag = 0x0112;
        using var img = System.Drawing.Image.FromFile(source);
        if (!img.PropertyIdList.Contains(orientationTag)) return false;
        var flip = img.GetPropertyItem(orientationTag)?.Value?[0] switch
        {
            2 => System.Drawing.RotateFlipType.RotateNoneFlipX,
            3 => System.Drawing.RotateFlipType.Rotate180FlipNone,
            4 => System.Drawing.RotateFlipType.Rotate180FlipX,
            5 => System.Drawing.RotateFlipType.Rotate90FlipX,
            6 => System.Drawing.RotateFlipType.Rotate90FlipNone,
            7 => System.Drawing.RotateFlipType.Rotate270FlipX,
            8 => System.Drawing.RotateFlipType.Rotate270FlipNone,
            _ => System.Drawing.RotateFlipType.RotateNoneFlipNone,
        };
        if (flip == System.Drawing.RotateFlipType.RotateNoneFlipNone) return false;
        img.RotateFlip(flip);
        img.RemovePropertyItem(orientationTag);
        var codec = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
        using var quality = new System.Drawing.Imaging.EncoderParameters(1);
        quality.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
        img.Save(jpeg, codec, quality);
        return true;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void ToPng(string source, Stream png)
    {
        using var bmp = System.Drawing.Image.FromFile(source);
        bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
    }

    protected override void Render(string source, string tmp, CancellationToken ct)
    {
        const double margin = 18, max = 842; // anything bigger than A4's long side is fitted onto A4
        using var png = new MemoryStream();
        using var img = Load(source, png);
        double w = img.PointWidth, h = img.PointHeight;
        if (w < 1 || h < 1) throw new InvalidDataException("image has no size");
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        if (w > max || h > max)
        {
            page.Size = PdfSharp.PageSize.A4;
            page.Orientation = w > h ? PdfSharp.PageOrientation.Landscape : PdfSharp.PageOrientation.Portrait;
            var k = Math.Min((page.Width.Point - 2 * margin) / w, (page.Height.Point - 2 * margin) / h);
            w *= k; h *= k;
        }
        else
        {
            page.Width = XUnit.FromPoint(w + 2 * margin);
            page.Height = XUnit.FromPoint(h + 2 * margin);
        }
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawImage(img, (page.Width.Point - w) / 2, (page.Height.Point - h) / 2, w, h);
        ct.ThrowIfCancellationRequested();
        doc.Save(tmp);
    }
}

internal sealed class TextConverter : NativeConverter
{
    // shortcut: .csv is rendered as plain text lines, not a table; upgrade if columns matter.
    protected override string[] Extensions => [".txt", ".log", ".csv"];

    protected override void Render(string source, string tmp, CancellationToken ct)
    {
        using var pager = new Pager(ct);
        var font = Pager.Font(NativeFonts.Mono, 9);
        using var reader = new StreamReader(source, TextFile.Detect(source), detectEncodingFromByteOrderMarks: true);
        while (reader.ReadLine() is { } line) pager.Block(line, font, mono: true);
        pager.Save(tmp);
    }
}

internal sealed class MarkdownConverter : NativeConverter
{
    protected override string[] Extensions => [".md"];

    protected override void Render(string source, string tmp, CancellationToken ct)
    {
        var doc = Markdown.Parse(File.ReadAllText(source, TextFile.Detect(source)));
        using var pager = new Pager(ct);
        foreach (var block in doc) Draw(pager, block, 0);
        pager.Save(tmp);
    }

    private static void Draw(Pager p, Block block, double indent)
    {
        switch (block)
        {
            case HeadingBlock h:
                double size = Math.Max(11, 24 - 3 * h.Level);
                p.Block(InlineText(h.Inline), Pager.Font(NativeFonts.Sans, size, bold: true), indent, before: size / 2);
                break;
            case ListBlock list:
                var n = 1;
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var bullet = list.IsOrdered ? $"{n++}. " : "• ";
                    var first = true;
                    foreach (var child in item)
                    {
                        if (first && child is LeafBlock leaf && leaf is not CodeBlock)
                            p.Block(bullet + InlineText(leaf.Inline), Pager.Font(NativeFonts.Sans, 11), indent + 12);
                        else Draw(p, child, indent + 24);
                        first = false;
                    }
                }
                break;
            case CodeBlock code: // fenced and indented
                p.Block(code.Lines.ToString(), Pager.Font(NativeFonts.Mono, 9), indent + 8, before: 4, mono: true);
                break;
            case QuoteBlock q:
                foreach (var child in q) Draw(p, child, indent + 18);
                break;
            case ThematicBreakBlock:
                p.Block(new string('—', 30), Pager.Font(NativeFonts.Sans, 11), indent);
                break;
            case ContainerBlock c:
                foreach (var child in c) Draw(p, child, indent);
                break;
            case LeafBlock leaf:
                p.Block(leaf.Inline is null ? leaf.Lines.ToString() : InlineText(leaf.Inline), Pager.Font(NativeFonts.Sans, 11), indent, before: 6);
                break;
        }
    }

    // shortcut: inline bold/italic/links are flattened to plain text; upgrade for styled runs.
    private static string InlineText(ContainerInline? inline)
    {
        var sb = new StringBuilder();
        foreach (var i in inline?.Descendants<Inline>() ?? [])
            switch (i)
            {
                case LiteralInline l: sb.Append(l.Content.ToString()); break;
                case CodeInline c: sb.Append(c.Content); break;
                case LineBreakInline: sb.Append(' '); break;
            }
        return sb.ToString();
    }
}

internal sealed partial class HtmlConverter : NativeConverter
{
    // shortcut: only visible text is extracted (tags, script, style, comments dropped, entities decoded);
    // layout, CSS, tables and images are not reproduced. Charset comes from the BOM, else UTF-8, else the ANSI code page (no <meta charset>).
    protected override string[] Extensions => [".html", ".htm"];

    [GeneratedRegex(@"<(script|style)\b.*?</\1\s*>|<!--.*?-->", RegexOptions.Singleline | RegexOptions.IgnoreCase, 5000)]
    private static partial Regex Hidden();
    [GeneratedRegex(@"</(p|h[1-6]|div|table|ul|ol|pre|blockquote)\s*>|<br\s*/?>|</(li|tr)\s*>", RegexOptions.IgnoreCase, 5000)]
    private static partial Regex Breaks();
    [GeneratedRegex("<[^>]*>", RegexOptions.None, 5000)]
    private static partial Regex Tags();

    protected override void Render(string source, string tmp, CancellationToken ct)
    {
        var html = Hidden().Replace(File.ReadAllText(source, TextFile.Detect(source)), " ");
        html = Breaks().Replace(html, m => m.Value.StartsWith("</li", StringComparison.OrdinalIgnoreCase)
            || m.Value.StartsWith("</tr", StringComparison.OrdinalIgnoreCase) || m.Value.StartsWith("<br", StringComparison.OrdinalIgnoreCase) ? "\n" : "\n\n");
        var text = WebUtility.HtmlDecode(Tags().Replace(html, ""));
        using var pager = new Pager(ct);
        var font = Pager.Font(NativeFonts.Sans, 11);
        foreach (var line in text.Split('\n'))
            pager.Block(Regex.Replace(line, @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(5)).Trim(), font);
        pager.Save(tmp);
    }
}

internal static class TextFile
{
    static TextFile() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>The BOM's encoding, else UTF-8 if every byte is valid UTF-8, else the ANSI code page (Excel "CSV", older Notepad files).</summary>
    public static Encoding Detect(string path)
    {
        using var reader = new StreamReader(path, new UTF8Encoding(false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
        var buffer = new char[8192];
        try
        {
            while (reader.Read(buffer, 0, buffer.Length) > 0) { /* validating only */ }
            return reader.CurrentEncoding;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        }
    }
}

/// <summary>Minimal A4 text flow: wraps, paginates, honours cancellation per page.</summary>
internal sealed class Pager : IDisposable
{
    private const double Margin = 54;
    private readonly PdfDocument doc = new();
    private readonly CancellationToken ct;
    private PdfPage page = null!;
    private XGraphics gfx = null!;
    private double y;

    public Pager(CancellationToken ct)
    {
        NativeFonts.Ensure();
        this.ct = ct;
        NewPage();
    }

    public static XFont Font(string family, double size, bool bold = false) =>
        new(family, size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);

    private void NewPage()
    {
        ct.ThrowIfCancellationRequested();
        gfx?.Dispose();
        page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        gfx = XGraphics.FromPdfPage(page);
        y = Margin;
    }

    /// <summary>Draws one logical line (may wrap). Mono wraps at a fixed column, else at word boundaries.</summary>
    public void Block(string text, XFont font, double indent = 0, double before = 0, bool mono = false)
    {
        var width = page.Width.Point - 2 * Margin - indent;
        var lh = font.GetHeight();
        y += before;
        foreach (var raw in text.Split('\n'))
        {
            var line = Clean(raw);
            foreach (var part in mono ? Chunk(line, (int)Math.Max(1, width / gfx.MeasureString("0", font).Width)) : Wrap(line, font, width))
            {
                if (y + lh > page.Height.Point - Margin) NewPage();
                gfx.DrawString(part, font, XBrushes.Black, new XRect(Margin + indent, y, width, lh), XStringFormats.TopLeft);
                y += lh;
            }
        }
    }

    // Control chars (tab -> 4 spaces) would render as missing glyphs.
    private static string Clean(string s) => string.Concat(s.Select(CleanChar)).TrimEnd();

    private static string CleanChar(char c)
    {
        if (c == '\t') return "    ";
        return char.IsControl(c) ? " " : c.ToString();
    }

    private static IEnumerable<string> Chunk(string s, int cols)
    {
        if (s.Length == 0) yield return "";
        for (var i = 0; i < s.Length; i += cols) yield return s.Substring(i, Math.Min(cols, s.Length - i));
    }

    private IEnumerable<string> Wrap(string s, XFont font, double width)
    {
        var cur = "";
        foreach (var word in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var test = cur.Length == 0 ? word : cur + " " + word;
            if (gfx.MeasureString(test, font).Width <= width) { cur = test; continue; }
            if (cur.Length > 0) yield return cur;
            cur = word;
            while (gfx.MeasureString(cur, font).Width > width && cur.Length > 1) // word wider than the line
            {
                var n = cur.Length - 1;
                while (n > 1 && gfx.MeasureString(cur[..n], font).Width > width) n--;
                yield return cur[..n];
                cur = cur[n..];
            }
        }
        yield return cur;
    }

    public void Save(string path)
    {
        gfx.Dispose();
        gfx = null!;
        doc.Save(path);
    }

    public void Dispose()
    {
        gfx?.Dispose();
        doc.Dispose();
    }
}
