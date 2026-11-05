using PDFile.Core.Diagnostics;

namespace PDFile.Core;

public sealed record ScanResult(IReadOnlyList<ConversionJob> Jobs, int SkippedCount);

public static class FolderScanner
{
    public static ScanResult Scan(string root, bool includeSubfolders, Func<string, bool> canConvert)
    {
        var jobs = new List<ConversionJob>();
        var skipped = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            try
            {
                foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    if (entry.Attributes.HasFlag(FileAttributes.Hidden) || entry.Attributes.HasFlag(FileAttributes.System))
                    {
                        if (entry is FileInfo) skipped++;
                    }
                    else if (entry is DirectoryInfo)
                    {
                        if (includeSubfolders && !entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) pending.Push(entry.FullName);
                    }
                    else if (entry.Name.StartsWith("~$", StringComparison.Ordinal) || !canConvert(entry.Extension.ToLowerInvariant()))
                    {
                        skipped++;
                    }
                    else
                    {
                        jobs.Add(new ConversionJob(entry.FullName, Path.GetRelativePath(root, entry.FullName)));
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Cannot read folder {dir}: {e.Message}");
            }
        }
        jobs.Sort((a, b) => NaturalCompare(a.RelativePath, b.RelativePath));
        return new ScanResult(jobs, skipped);
    }

    /// <summary>Case-insensitive; digit runs compare by value, so "2" sorts before "10".</summary>
    public static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                var si = i; var sj = j;
                while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0'); var nb = b[sj..j].TrimStart('0');
                var c = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : string.CompareOrdinal(na, nb);
                if (c != 0) return c;
            }
            else
            {
                var c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (c != 0) return c;
                i++; j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
