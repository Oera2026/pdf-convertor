using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PDFile.Core.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PDFile.App;

public sealed partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm)
    {
        Vm = vm;
        InitializeComponent();
        Title = "PDFile";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 780));
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);
        VersionText.Text = "v" + typeof(MainWindow).Assembly.GetName().Version?.ToString(3);

        // Handled-too: the reorderable ListView may mark drag events handled before they reach the grid.
        RootGrid.AddHandler(UIElement.DragOverEvent, new DragEventHandler(OnDragOver), true);
        RootGrid.AddHandler(UIElement.DropEvent, new DragEventHandler(OnDrop), true);

        if (SessionMarker.PreviousSessionEndedAbnormally(out var detail))
        {
            SessionBar.Message = string.IsNullOrWhiteSpace(detail) ? "No details were recorded." : detail;
            SessionBar.IsOpen = true;
        }
        // Office converters are only registered when Office is installed.
        OfficeBar.IsOpen = !App.Converters.Any(c => c.CanConvert(".docx") || c.CanConvert(".xlsx") || c.CanConvert(".pptx"));
        RootGrid.Loaded += (_, _) => Safe.Run(() => TestCrash.AfterWindowShown(DispatcherQueue));
    }

    public MainViewModel Vm { get; }

    private nint Hwnd => WindowNative.GetWindowHandle(this);

    private void OnChooseFolder(object sender, RoutedEventArgs e) => Safe.Fire(async () =>
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, Hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) await Vm.LoadFolderAsync(folder.Path);
    });

    private void OnIncludeSubfolders(object sender, RoutedEventArgs e) => Safe.Fire(() =>
    {
        Vm.IncludeSubfolders = ((CheckBox)sender).IsChecked == true;
        return Vm.RescanAsync();
    });

    private void OnModeChecked(object sender, RoutedEventArgs e) => Safe.Run(() => Vm.SetMode(ReferenceEquals(sender, MergedRadio)));

    private void OnChangeOutput(object sender, RoutedEventArgs e) => Safe.Fire(async () =>
    {
        string? chosen;
        if (Vm.IsMerged)
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = "Merged" };
            picker.FileTypeChoices.Add("PDF document", [".pdf"]);
            InitializeWithWindow.Initialize(picker, Hwnd);
            var file = await picker.PickSaveFileAsync();
            chosen = file?.Path;
            // The picker creates an empty placeholder file; remove it so the converter does not treat the name as taken.
            if (chosen is not null && new FileInfo(chosen).Length == 0) File.Delete(chosen);
        }
        else
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, Hwnd);
            chosen = (await picker.PickSingleFolderAsync())?.Path;
        }
        if (chosen is not null) Vm.SetOutput(chosen);
    });

    private void OnConvertOrStop(object sender, RoutedEventArgs e) => Safe.Fire(Vm.ConvertOrStopAsync);

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!Vm.IsIdle || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Add to PDFile";
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!Vm.IsIdle || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var deferral = e.GetDeferral();
        Safe.Fire(async () =>
        {
            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var folder = items.OfType<StorageFolder>().FirstOrDefault(); // one folder wins over loose files
                if (folder is not null) await Vm.LoadFolderAsync(folder.Path);
                else Vm.LoadFiles(items.OfType<StorageFile>().Select(f => f.Path).ToList());
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    private void OnCopyFailure(object sender, RoutedEventArgs e) =>
        Safe.Run(() => Shell.CopyText(((FrameworkElement)sender).DataContext is FailureItem f ? $"{f.Name}{Environment.NewLine}{f.Report}" : throw new InvalidOperationException("Copy button has no failure attached.")));

    private void OnCopyAllFailures(object sender, RoutedEventArgs e) => Safe.Run(() => Shell.CopyText(Vm.AllFailuresText));

    private void OnOpenOutput(object sender, RoutedEventArgs e) => Safe.Run(() =>
    {
        var folder = Vm.OutputFolderToOpen;
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder); // nothing was written if every file failed
        if (!string.IsNullOrEmpty(folder)) Shell.Open(folder);
    });

    private void OnOpenMerged(object sender, RoutedEventArgs e) => Safe.Run(() => Shell.Open(Vm.MergedPdfPath!));

    private void OnDismissSummary(object sender, RoutedEventArgs e) => Safe.Run(Vm.DismissSummary);

    private void OnOpenLogs(object sender, RoutedEventArgs e) => Safe.Run(() =>
    {
        Directory.CreateDirectory(Log.LogDirectory);
        Shell.Open(Log.LogDirectory);
    });
}
