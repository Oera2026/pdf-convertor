using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using PDFile.Core;
using PDFile.Core.Diagnostics;

namespace PDFile.Office;

/// <summary>
/// One Office application driven through late-bound COM on its own STA thread. The instance is started lazily and reused.
/// Assumes ConvertAsync is called sequentially (one file at a time per app): the timeout covers queue wait too, and a
/// timeout retires the whole instance.
/// </summary>
internal abstract class OfficeApp : IConverter, IAsyncDisposable, IDisposable
{
    private sealed class Session
    {
        public required StaWorker Worker { get; init; }
        public object? App;                       // touched on the worker thread only (and released when it exits)
        public List<Process> Owned { get; } = [];  // processes WE launched; the only ones we ever kill
        public bool Attached;                     // attached to an instance the user already had running
        public string Version = "?";
        public volatile string Stage = "start";
        public volatile bool Broken;              // set by the worker; caller retires the session afterwards
    }

    private readonly object _gate = new();
    private Session? _session;
    private bool _disposed;

    protected abstract string Name { get; }
    protected abstract string ProcessName { get; }
    internal abstract string ProgId { get; }
    protected abstract IReadOnlyList<string> Extensions { get; }

    protected OfficeApp() => AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

    protected abstract void Configure(dynamic app, bool owned);
    protected abstract object Open(dynamic app, string path);
    protected abstract void Export(dynamic doc, string dest);
    protected abstract void Close(dynamic doc);
    protected virtual void Quit(dynamic app) => app.Quit();

    public bool CanConvert(string extension) => Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    public async Task ConvertAsync(string sourcePath, string destPdfPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(sourcePath))
            throw new ConversionException("open", $"{Name}: source file not found: '{sourcePath}'");
        long size = new FileInfo(sourcePath).Length;

        Session s = GetSession();
        Task<int> work = s.Worker.RunAsync(() => ConvertOnWorker(s, sourcePath, destPdfPath, size));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(OfficeConverters.FileTimeout);
        try
        {
            await work.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!work.IsCompleted)
        {
            // Hung or cancelled: kill what we launched, abandon the stuck STA thread (it unblocks when the process dies).
            string stage = s.Stage;
            bool killed = s.Owned.Count > 0;
            Retire(s, ct.IsCancellationRequested ? "cancelled" : "timeout");
            _ = ObserveLateAsync(work);
            if (ct.IsCancellationRequested) throw;
            string msg = OfficeDiagnostics.FormatTimeout(Name, s.Version, stage, OfficeConverters.FileTimeout, sourcePath, size, killed);
            Log.Error(msg);
            throw new ConversionException(stage, msg) { TimedOut = true };
        }
        finally
        {
            if (s.Broken) Retire(s, "broken after error");
        }
    }

    private int ConvertOnWorker(Session s, string src, string dest, long size)
    {
        BatchState.SetStage($"{Name}: {Path.GetFileName(src)}");
        if (s.App is null) Step(s, "start", src, size, () => { StartApp(s); return 0; });
        dynamic app = s.App!;
        object doc = Step(s, "open", src, size, () => Open(app, src));
        try
        {
            ConversionException? exportError = null;
            try
            {
                Step(s, "export", src, size, () =>
                {
                    Export(doc, dest);
                    if (!File.Exists(dest) || new FileInfo(dest).Length == 0)
                        throw new InvalidOperationException("Office reported success but wrote no PDF file.");
                    return 0;
                });
            }
            catch (ConversionException ex)
            {
                exportError = ex;
            }

            try
            {
                Step(s, "close", src, size, () => { Close(doc); return 0; });
            }
            catch (ConversionException closeError) when (exportError is not null)
            {
                Log.Error($"{Name}: close also failed after export error (original error is reported)", closeError); // not lost, just secondary
            }

            if (exportError is not null) ExceptionDispatchInfo.Capture(exportError).Throw();
            return 0;
        }
        finally
        {
            if (Marshal.IsComObject(doc)) Marshal.FinalReleaseComObject(doc);
        }
    }

    private T Step<T>(Session s, string stage, string src, long size, Func<T> action)
    {
        s.Stage = stage;
        try { return action(); }
        catch (Exception ex) when (ex is not ConversionException) { throw Wrap(s, stage, src, size, ex); }
    }

    private ConversionException Wrap(Session s, string stage, string src, long size, Exception ex)
    {
        int hr = ex.HResult;
        bool motw = stage == "open" && File.Exists(src + ":Zone.Identifier"); // NTFS alternate data stream = downloaded file
        string msg = OfficeDiagnostics.FormatFailure(Name, s.Version, stage, src, size, hr, ex.Message,
            OfficeDiagnostics.Hint(hr, ex.Message, motw));
        if (stage is "start" or "close" || OfficeDiagnostics.IsServerDead(hr)) s.Broken = true;
        Log.Error(msg, ex);
        return new ConversionException(stage, msg, ex, hr);
    }

    private void StartApp(Session s)
    {
        HashSet<int> before = SnapshotPids();
        Type type = Type.GetTypeFromProgID(ProgId) ?? throw new InvalidOperationException($"ProgID {ProgId} is not registered.");
        object app = Activator.CreateInstance(type) ?? throw new InvalidOperationException($"Could not create {ProgId}.");
        s.App = app;
        foreach (Process p in Process.GetProcessesByName(ProcessName))
        {
            if (before.Contains(p.Id)) p.Dispose(); else s.Owned.Add(p);
        }
        // shortcut: if the user's instance was already running we attach to it (COM does that) and never kill/quit it.
        s.Attached = s.Owned.Count == 0;
        Configure(app, !s.Attached);
        s.Version = ((dynamic)app).Version as string ?? "?";
    }

    private HashSet<int> SnapshotPids()
    {
        var ids = new HashSet<int>();
        foreach (Process p in Process.GetProcessesByName(ProcessName)) { ids.Add(p.Id); p.Dispose(); }
        return ids;
    }

    private Session GetSession()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is not null) return _session;
            Session? created = null;
            created = new Session
            {
                Worker = new StaWorker($"PDFile {Name} STA", () => ReleaseApp(created!)),
            };
            return _session = created;
        }
    }

    /// <summary>Drops the session: no more work, kill the Office processes we started. The worker thread releases COM when it unblocks.</summary>
    private void Retire(Session s, string reason)
    {
        lock (_gate) { if (ReferenceEquals(_session, s)) _session = null; }
        s.Worker.Complete();
        KillOwned(s);
        Log.Warn($"{Name}: instance retired ({reason}); the next file starts a fresh one.");
    }

    private void KillOwned(Session s)
    {
        foreach (Process p in s.Owned)
        {
            try { if (!p.HasExited) p.Kill(true); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                Log.Error($"{Name}: could not kill process {p.Id}", ex);
            }
        }
    }

    private void ReleaseApp(Session s)
    {
        try { if (s.App is not null && Marshal.IsComObject(s.App)) Marshal.FinalReleaseComObject(s.App); }
        catch (Exception ex) { Log.Error($"{Name}: releasing COM instance failed", ex); } // runs on a bare thread: must not throw
    }

    private static async Task ObserveLateAsync(Task t)
    {
#pragma warning disable VSTHRD003 // task comes from our own STA worker, no sync context involved
        try { await t.ConfigureAwait(false); }
#pragma warning restore VSTHRD003
        catch (Exception ex) { Log.Error("Abandoned Office call finished with an error", ex); }
    }

    private void OnProcessExit(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        Session? s;
        lock (_gate) { _disposed = true; s = _session; _session = null; }
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        if (s is null) return;
        if (!s.Attached)
            _ = ObserveLateAsync(s.Worker.RunAsync(() => { s.Stage = "quit"; Quit((dynamic)s.App!); return 0; }));
        s.Worker.Complete();
        bool quitInTime = s.Worker.Join(TimeSpan.FromSeconds(5));
        if (!quitInTime) Log.Warn($"{Name}: Quit did not finish in 5s, killing process.");
        foreach (Process p in s.Owned)
        {
            if (!quitInTime || !p.WaitForExit(3000)) KillOwned(s);
            p.Dispose();
        }
    }

    public ValueTask DisposeAsync() => new(Task.Run(Dispose));
}
