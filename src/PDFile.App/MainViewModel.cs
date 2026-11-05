using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using PDFile.Core;
using PDFile.Core.Diagnostics;
using PDFile.Office;

namespace PDFile.App;

public enum FileStatus { Ready, Converting, Done, Failed, Cancelled }

public sealed class FileItem(ConversionJob job) : Observable
{
    private bool _included = true;
    private FileStatus _status;

    public ConversionJob Job { get; } = job;
    public string RelativePath => Job.RelativePath;
    public string TypeIcon { get; } = Shell.IconForExtension(Path.GetExtension(job.SourcePath));

    public bool IsIncluded { get => _included; set => Set(ref _included, value); }

    public FileStatus Status
    {
        get => _status;
        set
        {
            if (!Set(ref _status, value)) return;
            Raise(nameof(StatusText));
            Raise(nameof(IsDone));
            Raise(nameof(IsFailed));
        }
    }

    public string StatusText => Status.ToString();
    public bool IsDone => Status == FileStatus.Done;
    public bool IsFailed => Status == FileStatus.Failed;
}

public sealed record FailureItem(string Name, string Headline, string Report);

public sealed class MainViewModel : Observable
{
    private const string SeparateFolderName = "PDF Output";
    private const string MergedFileName = "Merged.pdf";

    private readonly IReadOnlyList<IConverter> _converters;
    private readonly BatchRunner _runner;
    private CancellationTokenSource? _cts;
    private int _scanVersion;
    private string? _root;
    private bool _rootIsFolder; // false for dropped files: there is no folder to rescan
    private bool _scanning;
    private bool _includeSubfolders = true;
    private bool _isMerged;
    private string _separateOutput = "";
    private string _mergedOutput = "";
    private int _skipped;
    private bool _isRunning;
    private bool _isStopping;
    private string _progressText = "";
    private int _done;
    private int _total = 1;
    private string? _summaryText;
    private string? _mergedPdfPath;

    public MainViewModel(IReadOnlyList<IConverter> converters)
    {
        _converters = converters;
        _runner = new BatchRunner(converters, new PdfMerger());
        Items.CollectionChanged += (_, _) => RaiseCounts();
    }

    public ObservableCollection<FileItem> Items { get; } = [];
    public ObservableCollection<FailureItem> Failures { get; } = [];

    public bool IncludeSubfolders { get => _includeSubfolders; set => Set(ref _includeSubfolders, value); }
    public bool HasRoot => _root is not null;
    public bool IsEmpty => Items.Count == 0;
    public bool IsMerged => _isMerged;
    public bool IsIdle => !_isRunning;
    public bool IsRunning => _isRunning;
    public bool ShowSummary => _summaryText is not null;
    public bool ShowMergedPdf => _mergedPdfPath is not null;
    public bool HasFailures => Failures.Count > 0;
    public string SummaryText => _summaryText ?? "";
    public string ProgressText => _progressText;
    public double ProgressDone => _done;
    public double ProgressTotal => _total;
    public string OutputPath => _isMerged ? _mergedOutput : _separateOutput;
    public string OutputLabel => _isMerged ? "Save as" : "Output folder";
    public string ConvertLabel
    {
        get
        {
            if (_isStopping) return "Stopping...";
            return _isRunning ? "Stop" : "Convert";
        }
    }
    public string ConvertIcon => Shell.Icon(IconKey);
    private string IconKey
    {
        get
        {
            if (_isRunning) return "IconStop";
            return _isMerged ? "IconMerge" : "IconConvert";
        }
    }
    // Not while scanning: the list (and the output path, already switched to the new folder) would not match.
    public bool CanConvert => _isRunning ? !_isStopping : !_scanning && Items.Any(i => i.IsIncluded);

    public string EmptyHint
    {
        get
        {
            if (_scanning) return "Scanning...";
            return HasRoot ? "No supported files found in this folder." : "or drop a folder or files here";
        }
    }
    public string CountText
    {
        get
        {
            var ready = Items.Count(i => i.IsIncluded);
            return $"{ready} {(ready == 1 ? "file" : "files")} ready, {_skipped} skipped (unsupported)";
        }
    }

    public string? MergedPdfPath => _mergedPdfPath;
    public string? OutputFolderToOpen => _isMerged ? Path.GetDirectoryName(_mergedPdfPath ?? _mergedOutput) : _separateOutput;

    public void SetMode(bool merged)
    {
        if (_isMerged == merged) return;
        _isMerged = merged;
        Raise(nameof(IsMerged));
        Raise(nameof(OutputPath));
        Raise(nameof(OutputLabel));
        Raise(nameof(ConvertIcon));
    }

    public void SetOutput(string path)
    {
        if (_isMerged) _mergedOutput = path; else _separateOutput = path;
        Raise(nameof(OutputPath));
    }

    public void DismissSummary()
    {
        _summaryText = null;
        _mergedPdfPath = null;
        Failures.Clear();
        Raise(nameof(ShowSummary));
        Raise(nameof(ShowMergedPdf));
        Raise(nameof(HasFailures));
    }

    private bool CanConvertPath(string pathOrExt)
    {
        var ext = Path.GetExtension(pathOrExt).ToLowerInvariant();
        return _converters.Any(c => c.CanConvert(ext));
    }

    public Task LoadFolderAsync(string folder)
    {
        _root = folder;
        _rootIsFolder = true;
        _separateOutput = Path.Combine(folder, SeparateFolderName);
        _mergedOutput = Path.Combine(folder, MergedFileName);
        Raise(nameof(HasRoot));
        Raise(nameof(OutputPath));
        return RescanAsync();
    }

    /// <summary>Scans the chosen folder again (also used when "Include subfolders" changes).</summary>
    public async Task RescanAsync()
    {
        if (_root is not { } root || !_rootIsFolder || _isRunning) return;
        var version = ++_scanVersion;
        var includeSub = _includeSubfolders;
        SetScanning(true);
        var result = await Task.Run(() => FolderScanner.Scan(root, includeSub, CanConvertPath), CancellationToken.None);
        if (version != _scanVersion) return; // a newer scan or a file drop superseded this one
        _scanning = false; // Show raises the dependent properties
        // Our own outputs would otherwise show up as input files on a rescan.
        var jobs = result.Jobs.Where(j => !j.RelativePath.StartsWith(SeparateFolderName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                                          && !j.RelativePath.Equals(MergedFileName, StringComparison.OrdinalIgnoreCase)).ToList();
        Show(jobs, result.SkippedCount + result.Jobs.Count - jobs.Count);
    }

    public void LoadFiles(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return;
        _scanVersion++;
        _scanning = false;
        _root = Path.GetDirectoryName(files[0]) ?? files[0];
        _rootIsFolder = false;
        _separateOutput = Path.Combine(_root, SeparateFolderName);
        _mergedOutput = Path.Combine(_root, MergedFileName);
        var supported = files.Distinct(StringComparer.OrdinalIgnoreCase).Where(CanConvertPath).ToList();
        Raise(nameof(HasRoot));
        Raise(nameof(OutputPath));
        Show(supported.Select(f => new ConversionJob(f, Path.GetFileName(f))).ToList(), files.Count - supported.Count);
    }

    private void Show(IReadOnlyList<ConversionJob> jobs, int skipped)
    {
        DismissSummary();
        _skipped = skipped;
        Items.Clear();
        foreach (var job in jobs)
        {
            var item = new FileItem(job);
            item.PropertyChanged += OnItemChanged;
            Items.Add(item);
        }
        Raise(nameof(EmptyHint));
        RaiseCounts();
    }

    private void SetScanning(bool scanning)
    {
        _scanning = scanning;
        Raise(nameof(EmptyHint));
        Raise(nameof(CanConvert));
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileItem.IsIncluded)) RaiseCounts();
    }

    private void RaiseCounts()
    {
        Raise(nameof(IsEmpty));
        Raise(nameof(CountText));
        Raise(nameof(CanConvert));
    }

    /// <summary>Starts the batch, or requests a stop if one is running.</summary>
    public async Task ConvertOrStopAsync()
    {
        if (_cts is { } running)
        {
            _isStopping = true;
            await running.CancelAsync();
            Raise(nameof(ConvertLabel));
            Raise(nameof(CanConvert));
            return;
        }
        var selected = Items.Where(i => i.IsIncluded).ToList();
        if (selected.Count == 0) return;
        DismissSummary();
        foreach (var item in selected) item.Status = FileStatus.Ready;
        var byJob = selected.ToDictionary(i => i.Job); // not by name: dropped files from search results can share one
        var options = new BatchOptions(selected.Select(i => i.Job).ToList(), _isMerged ? OutputMode.MergedPdf : OutputMode.SeparatePdfs, OutputPath);
        using var cts = new CancellationTokenSource();
        _cts = cts;
        SetRunning(true);
        _total = Math.Max(1, selected.Count);
        _done = 0;
        SetProgress(0, "Starting...");
        // Progress<T> captures the UI synchronization context here, so callbacks arrive on the UI thread.
        var progress = new Progress<BatchProgress>(p => OnProgress(p, byJob));
        BatchSummary? summary = null;
        try
        {
            summary = await Task.Run(() => _runner.RunAsync(options, progress, cts.Token), cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Log.Info("Batch cancelled by user (runner threw OperationCanceledException)");
        }
        finally
        {
            // A hidden Office left running between batches can swallow files the user opens from Explorer.
            await Task.Run(() => OfficeConverters.EndSessions(_converters), CancellationToken.None);
            _cts = null;
            _isStopping = false;
            SetRunning(false);
        }
        if (summary is null)
        {
            foreach (var item in selected.Where(i => i.Status is FileStatus.Ready or FileStatus.Converting)) item.Status = FileStatus.Cancelled;
            return;
        }
        Finish(summary, byJob);
    }

    private void OnProgress(BatchProgress p, Dictionary<ConversionJob, FileItem> byJob)
    {
        var converting = p.Stage == "Converting";
        if (p.Job is { } job && byJob.TryGetValue(job, out var item))
        {
            item.Status = p.Stage switch
            {
                "Converting" => FileStatus.Converting,
                nameof(Outcome.Succeeded) => FileStatus.Done,
                nameof(Outcome.Failed) => FileStatus.Failed,
                nameof(Outcome.Cancelled) => FileStatus.Cancelled,
                _ => item.Status,
            };
        }
        _total = Math.Max(1, p.Total);
        SetProgress(Math.Min(p.Done + (converting ? 1 : 0), p.Total), $"{Math.Min(p.Done + (converting ? 1 : 0), p.Total)} of {p.Total} – {p.CurrentFile} – {p.Stage.ToLowerInvariant()}");
    }

    private void Finish(BatchSummary summary, Dictionary<ConversionJob, FileItem> byJob)
    {
        Failures.Clear();
        foreach (var r in summary.Results)
        {
            if (!byJob.TryGetValue(r.Job, out var item)) continue;
            item.Status = r.Outcome switch { Outcome.Succeeded => FileStatus.Done, Outcome.Failed => FileStatus.Failed, _ => FileStatus.Cancelled };
            if (r.Outcome != Outcome.Failed) continue;
            var report = r.ErrorReport ?? "(no error report was produced)";
            var headline = report.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "Failed";
            Failures.Add(new FailureItem(r.Job.RelativePath, headline, report));
        }
        var ok = summary.Results.Count(r => r.Outcome == Outcome.Succeeded);
        var cancelled = summary.Results.Count(r => r.Outcome == Outcome.Cancelled);
        _summaryText = $"{ok} converted, {Failures.Count} failed" + (cancelled > 0 ? $", {cancelled} cancelled" : "");
        _mergedPdfPath = summary.MergedPdfPath;
        SetProgress(_total, "");
        Raise(nameof(SummaryText));
        Raise(nameof(ShowSummary));
        Raise(nameof(ShowMergedPdf));
        Raise(nameof(HasFailures));
        Raise(nameof(MergedPdfPath));
        Raise(nameof(OutputFolderToOpen));
    }

    public string AllFailuresText => string.Join(Environment.NewLine + new string('=', 72) + Environment.NewLine,
        Failures.Select(f => $"{f.Name}{Environment.NewLine}{f.Report}"));

    private void SetRunning(bool running)
    {
        _isRunning = running;
        Raise(nameof(IsRunning));
        Raise(nameof(IsIdle));
        Raise(nameof(ConvertLabel));
        Raise(nameof(ConvertIcon));
        Raise(nameof(CanConvert));
    }

    private void SetProgress(int done, string text)
    {
        _done = done;
        _progressText = text;
        Raise(nameof(ProgressDone));
        Raise(nameof(ProgressTotal));
        Raise(nameof(ProgressText));
    }
}
