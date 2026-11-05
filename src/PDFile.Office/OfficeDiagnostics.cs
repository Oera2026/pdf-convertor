using System.Globalization;

namespace PDFile.Office;

/// <summary>Pure, platform-independent helpers (HRESULT hints, message formatting, path parsing). No COM here.</summary>
internal static class OfficeDiagnostics
{
    internal sealed record InstalledApp(string Name, string ProgId, bool Registered, string? CurVer, string? Exe, string? FileVersion);

    internal static string Hr(int hr) => "0x" + hr.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>RPC errors meaning the Office process is gone or unreachable.</summary>
    internal static bool IsServerDead(int hr) =>
        (uint)hr is 0x800706BA or 0x800706BE or 0x80010108 or 0x80010105 or 0x800706BF or 0x80010012;

    /// <summary>Human-readable cause for a failure, or empty. Keyword checks are English-only; the HRESULT checks are not.</summary>
    internal static string Hint(int hr, string message, bool markOfTheWeb)
    {
        bool Has(params string[] words) => words.Any(w => message.Contains(w, StringComparison.OrdinalIgnoreCase));
        string hint = (uint)hr switch
        {
            0x800706BA or 0x800706BE or 0x80010108 or 0x80010105 or 0x800706BF or 0x80010012 =>
                "The Office process died or was closed (RPC server unavailable). A fresh instance is started for the next file.",
            0x80040154 => "Office is not installed or not registered for COM automation.",
            0x80080005 => "Office could not start (another instance with different elevation, a first-run/activation dialog, or a broken install). Start the app once manually.",
            0x80010001 or 0x8001010A => "Office is busy, usually a modal dialog is open. Close it and retry.",
            0x80070020 or 0x80070021 => "The file is locked or in use by another program (for example open in Word/Excel).",
            0x800A1520 => "The document is password-protected (or the password is wrong).",
            _ => "",
        };
        if (hint.Length > 0) return hint;
        if (Has("password", "encrypt")) return "The document is password-protected.";
        if (Has("being used by another", "sharing violation", "locked for editing", "in use")) return "The file is locked or in use by another program.";
        if (Has("corrupt", "damaged", "unreadable content", "not a valid", "file format is not valid"))
            return "The file appears to be corrupt or in an unsupported format.";
        if (Has("protected view", "file block", "blocked", "trust center"))
            return "The file is blocked by Office (Protected View / File Block / Trust Center). Unblock it in file Properties or Trust Center settings.";
        if (Has("activat", "licens", "unlicensed", "first run")) return "Office is not activated or is showing a first-run dialog. Open the app once manually.";
        return markOfTheWeb ? "The file came from the internet (Mark of the Web); Office may block it. Unblock it in file Properties." : "";
    }

    internal static string FormatFailure(string app, string version, string stage, string src, long size, int hr, string detail, string hint)
    {
        string s = string.Create(CultureInfo.InvariantCulture,
            $"{app} {version}: {stage} failed for '{src}' ({size:N0} bytes), HRESULT {Hr(hr)}: {detail.Trim()}");
        return hint.Length > 0 ? s + " Hint: " + hint : s;
    }

    internal static string FormatTimeout(string app, string version, string stage, TimeSpan timeout, string src, long size, bool killed)
    {
        string s = string.Create(CultureInfo.InvariantCulture,
            $"{app} {version}: timed out after {timeout.TotalSeconds:0}s in stage '{stage}' for '{src}' ({size:N0} bytes). ")
            + (killed ? "The Office process was killed" : "The Office instance was not started by PDFile so it was NOT killed (it may stay stuck)")
            + "; a fresh instance is used for the next file.";
        return stage == "open" ? s + " Typical causes: password prompt, Protected View or a modal dialog." : s;
    }

    /// <summary>Pulls the exe path out of a LocalServer32 value such as: "C:\x\WINWORD.EXE" /Automation.</summary>
    internal static string? ExtractExePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        string c = command.Trim();
        if (c[0] == '"')
        {
            int end = c.IndexOf('"', 1);
            return end > 1 ? c[1..end] : c.Trim('"');
        }
        int exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe >= 0 ? c[..(exe + 4)] : c;
    }

    internal static string FormatInstalled(IEnumerable<InstalledApp> apps) =>
        string.Join(Environment.NewLine, apps.Select(a => a.Registered
            ? $"{a.Name}: installed, ProgID={a.CurVer ?? a.ProgId}, version={a.FileVersion ?? "unknown"}, exe={a.Exe ?? "unknown"}"
            : $"{a.Name}: not registered ({a.ProgId})"));
}
