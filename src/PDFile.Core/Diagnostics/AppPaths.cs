namespace PDFile.Core.Diagnostics;

internal static class AppPaths
{
    /// <summary>%LOCALAPPDATA%\PDFile; PDFILE_DATA overrides it (tests).</summary>
    public static string Root =>
        Environment.GetEnvironmentVariable("PDFILE_DATA") is { Length: > 0 } o
            ? o
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PDFile");
}
