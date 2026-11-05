using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace PDFile.App;

/// <summary>Small OS helpers shared by the window and the crash dialog.</summary>
internal static class Shell
{
    /// <summary>Opens a folder in Explorer or a file in its default app. Throws if it cannot.</summary>
    public static void Open(string path)
    {
        using var p = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public static void CopyText(string text)
    {
        var data = new DataPackage();
        data.SetText(text);
        Clipboard.SetContent(data);
        Clipboard.Flush(); // keep the text after the app exits (crash dialog)
    }

    public static string Icon(string key) => (string)Application.Current.Resources[key];

    public static string IconForExtension(string ext) => Icon(ext.ToLowerInvariant() switch
    {
        ".doc" or ".docx" or ".docm" or ".dot" or ".dotx" or ".rtf" or ".odt" => "IconWord",
        ".xls" or ".xlsx" or ".xlsm" or ".xlsb" or ".csv" or ".ods" => "IconSheet",
        ".ppt" or ".pptx" or ".pptm" or ".pps" or ".ppsx" or ".odp" => "IconSlides",
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff" or ".webp" => "IconImage",
        ".txt" or ".log" => "IconText",
        ".md" or ".markdown" or ".htm" or ".html" => "IconMarkup",
        ".pdf" => "IconPdf",
        _ => "IconUnknown",
    });
}
