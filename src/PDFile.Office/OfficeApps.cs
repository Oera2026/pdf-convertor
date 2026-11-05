namespace PDFile.Office;

// Constants: wdAlertsNone=0, wdExportFormatPDF=17, msoAutomationSecurityForceDisable=3, xlTypePDF=0,
// ppSaveAsPDF=32, ppFixedFormatTypePDF=2, ppAlertsNone=1.

internal sealed class WordApp : OfficeApp
{
    protected override string Name => "Word";
    protected override string ProcessName => "WINWORD";
    internal override string ProgId => "Word.Application";
    protected override IReadOnlyList<string> Extensions { get; } = [".doc", ".docx", ".rtf", ".odt"];

    protected override void Configure(dynamic app, bool owned)
    {
        if (owned) app.Visible = false;
        app.DisplayAlerts = 0;
        app.AutomationSecurity = 3;
    }

    // Read-only, no MRU, empty passwords (wrong password => error instead of a prompt).
    protected override object Open(dynamic app, string path) =>
        app.Documents.Open(path, false, true, false, "", "", false, "", "");

    protected override void Export(dynamic doc, string dest)
    {
        try { doc.ExportAsFixedFormat(dest, 17, false); }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            PDFile.Core.Diagnostics.Log.Error("Word ExportAsFixedFormat failed, falling back to SaveAs2", ex);
            doc.SaveAs2(dest, 17);
        }
    }

    protected override void Close(dynamic doc) => doc.Close(0);
    protected override int OpenDocuments(dynamic app) => app.Documents.Count;
    protected override void Quit(dynamic app) => app.Quit(0);
}

internal sealed class ExcelApp : OfficeApp
{
    protected override string Name => "Excel";
    protected override string ProcessName => "EXCEL";
    internal override string ProgId => "Excel.Application";
    protected override IReadOnlyList<string> Extensions { get; } = [".xls", ".xlsx", ".xlsm"];

    protected override void Configure(dynamic app, bool owned)
    {
        if (owned) app.Visible = false;
        app.DisplayAlerts = false;
        app.AutomationSecurity = 3;
        app.AskToUpdateLinks = false;
        app.EnableEvents = false;
    }

    // UpdateLinks=0 (never), ReadOnly, empty passwords, IgnoreReadOnlyRecommended, AddToMru=false.
    protected override object Open(dynamic app, string path) =>
        app.Workbooks.Open(path, 0, true, Type.Missing, "", "", true, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing, false);

    // shortcut: sheets are exported with their own page setup (no fit-to-1-page-wide: PageSetup is slow and needs a printer driver), revisit if wide sheets split badly.
    protected override void Export(dynamic doc, string dest) => doc.ExportAsFixedFormat(0, dest);

    protected override void Close(dynamic doc) => doc.Close(false);
    protected override int OpenDocuments(dynamic app) => app.Workbooks.Count; // automation instances load no XLStart/PERSONAL.XLSB
}

internal sealed class PowerPointApp : OfficeApp
{
    protected override string Name => "PowerPoint";
    protected override string ProcessName => "POWERPNT";
    internal override string ProgId => "PowerPoint.Application";
    protected override IReadOnlyList<string> Extensions { get; } = [".ppt", ".pptx"];

    // shortcut: PowerPoint rejects Visible=false, so only WithWindow=false is used; a password prompt there is caught by the timeout.
    protected override void Configure(dynamic app, bool owned)
    {
        app.DisplayAlerts = 1;
        app.AutomationSecurity = 3;
    }

    // ReadOnly=msoTrue(-1), Untitled=msoFalse, WithWindow=msoFalse.
    protected override object Open(dynamic app, string path) => app.Presentations.Open(path, -1, 0, 0);

    protected override void Export(dynamic doc, string dest)
    {
        try { doc.SaveAs(dest, 32); }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            PDFile.Core.Diagnostics.Log.Error("PowerPoint SaveAs(PDF) failed, falling back to ExportAsFixedFormat", ex);
            doc.ExportAsFixedFormat(dest, 2);
        }
    }

    protected override void Close(dynamic doc) => doc.Close();
    protected override int OpenDocuments(dynamic app) => app.Presentations.Count;
}
