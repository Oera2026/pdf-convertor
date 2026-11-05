using PdfSharp.Fonts;

namespace PDFile.Core.Native;

/// <summary>Registers a tiny PDFsharp font resolver over installed TrueType fonts: family "Sans" and "Mono".</summary>
internal static class NativeFonts
{
    public const string Sans = "Sans";
    public const string Mono = "Mono";

    // Candidate file sets (regular, bold, italic, bold-italic), first set whose regular file exists wins.
    private static readonly string[][] SansSets =
    [
        ["segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf"],
        ["arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"],
        ["DejaVuSans.ttf", "DejaVuSans-Bold.ttf", "DejaVuSans-Oblique.ttf", "DejaVuSans-BoldOblique.ttf"],
        ["LiberationSans-Regular.ttf", "LiberationSans-Bold.ttf", "LiberationSans-Italic.ttf", "LiberationSans-BoldItalic.ttf"],
    ];

    private static readonly string[][] MonoSets =
    [
        ["consola.ttf", "consolab.ttf", "consolai.ttf", "consolaz.ttf"],
        ["cour.ttf", "courbd.ttf", "couri.ttf", "courbi.ttf"],
        ["DejaVuSansMono.ttf", "DejaVuSansMono-Bold.ttf", "DejaVuSansMono-Oblique.ttf", "DejaVuSansMono-BoldOblique.ttf"],
        ["LiberationMono-Regular.ttf", "LiberationMono-Bold.ttf", "LiberationMono-Italic.ttf", "LiberationMono-BoldItalic.ttf"],
    ];

    private static readonly object Gate = new();
    private static bool done;

    /// <summary>Idempotent, thread-safe. Throws ConversionException("font") when no usable font exists.</summary>
    public static void Ensure()
    {
        lock (Gate)
        {
            if (done) return;
            if (GlobalFontSettings.FontResolver is null) GlobalFontSettings.FontResolver = new Resolver();
            done = true;
        }
    }

    private sealed class Resolver : IFontResolver
    {
        private readonly Dictionary<string, string> faces = new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> cache = new(StringComparer.Ordinal);

        public Resolver()
        {
            var dirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
                "/usr/share/fonts", "/usr/local/share/fonts", "/Library/Fonts",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts"),
            }.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)).ToArray();

            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in dirs)
                foreach (var f in Directory.EnumerateFiles(d, "*.ttf", opts))
                    files.TryAdd(Path.GetFileName(f), f);

            var sans = Register(Sans, SansSets, files);
            var mono = Register(Mono, MonoSets, files);
            if (!sans)
                throw new ConversionException("font",
                    "No usable font found. Searched for Segoe UI, Arial, DejaVu Sans, Liberation Sans in: " +
                    (dirs.Length == 0 ? "(no font directory exists)" : string.Join(", ", dirs)));
            if (!mono) // fall back to proportional text so Mono requests still render
                for (var i = 0; i < 4; i++) faces[$"{Mono}-{i}"] = faces[$"{Sans}-{i}"];
        }

        private bool Register(string family, string[][] sets, Dictionary<string, string> files)
        {
            foreach (var set in sets)
            {
                if (!files.TryGetValue(set[0], out var regular)) continue;
                for (var i = 0; i < 4; i++)
                    faces[$"{family}-{i}"] = files.TryGetValue(set[i], out var p) ? p : regular;
                return true;
            }
            return false;
        }

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
        {
            var family = familyName.Contains("mono", StringComparison.OrdinalIgnoreCase) ? Mono : Sans;
            return new FontResolverInfo($"{family}-{(bold ? 1 : 0) + (italic ? 2 : 0)}");
        }

        public byte[]? GetFont(string faceName)
        {
            lock (cache)
            {
                if (cache.TryGetValue(faceName, out var data)) return data;
                if (!faces.TryGetValue(faceName, out var path)) return null;
                return cache[faceName] = File.ReadAllBytes(path);
            }
        }
    }
}
