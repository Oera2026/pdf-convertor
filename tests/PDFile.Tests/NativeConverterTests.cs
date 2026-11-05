using PDFile.Core;
using PDFile.Core.Native;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PDFile.Tests;

public sealed class NativeConverterTests : IDisposable
{
    private readonly string dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pdfile-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(dir, true);

    private static readonly byte[] Png = System.Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private async Task<int> RunAsync(string name, byte[] content)
    {
        var src = Path.Combine(dir, name);
        await File.WriteAllBytesAsync(src, content);
        var dest = src + ".pdf";
        var ext = Path.GetExtension(name);
        await NativeConverters.All.Single(c => c.CanConvert(ext)).ConvertAsync(src, dest, CancellationToken.None);
        Assert.False(File.Exists(dest + ".tmp"));
        using var pdf = PdfReader.Open(dest, PdfDocumentOpenMode.Import);
        return pdf.PageCount;
    }

    private static byte[] Utf8(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    [Fact]
    public async Task Png_is_one_page() => Assert.Equal(1, await RunAsync("a.png", Png));

    [Theory]
    [InlineData("a.gif", "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7")]
    [InlineData("a.bmp", "Qk0+AAAAAAAAADYAAAAoAAAAAQAAAAEAAAABABgAAAAAAAgAAAAAAAAAAAAAAAAAAAAAAAAA////AA==")]
    public async Task Other_formats_need_Windows(string name, string b64)
    {
        var bytes = System.Convert.FromBase64String(b64);
        if (OperatingSystem.IsWindows()) Assert.Equal(1, await RunAsync(name, bytes));
        else await Assert.ThrowsAsync<ConversionException>(() => RunAsync(name, bytes)); // System.Drawing is Windows-only
    }

    [Fact]
    public async Task Text_with_500_lines_paginates()
    {
        var text = string.Join("\n", Enumerable.Range(0, 500).Select(i => $"line {i} é中"));
        Assert.True(await RunAsync("a.txt", Utf8(text)) > 1);
    }

    [Fact]
    public async Task Markdown_renders() =>
        Assert.True(await RunAsync("a.md", Utf8("# Title\n\n- one\n- two\n\n```\ncode here\n```\n\nPara **bold**.")) >= 1);

    [Fact]
    public async Task Html_with_script_renders() =>
        Assert.True(await RunAsync("a.html", Utf8("<html><script>var x=1;</script><body><p>Hi &amp; bye</p></body></html>")) >= 1);

    [Fact]
    public async Task Pdf_is_copied()
    {
        var src = Path.Combine(dir, "in.pdf");
        await RunAsync("seed.txt", Utf8("x"));
        File.Copy(Path.Combine(dir, "seed.txt.pdf"), src);
        Assert.Equal(1, await RunAsync("in.pdf", await File.ReadAllBytesAsync(src)));
    }

    [Fact]
    public async Task Corrupt_image_throws_ConversionException()
    {
        var ex = await Assert.ThrowsAsync<ConversionException>(() => RunAsync("bad.png", Utf8("not a png")));
        Assert.Contains("bad.png", ex.Message);
    }

    [Fact]
    public async Task Cancelled_leaves_no_file()
    {
        var src = Path.Combine(dir, "c.txt");
        await File.WriteAllTextAsync(src, "x");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NativeConverters.All.Single(c => c.CanConvert(".txt")).ConvertAsync(src, src + ".pdf", cts.Token));
        Assert.False(File.Exists(src + ".pdf"));
    }
}
