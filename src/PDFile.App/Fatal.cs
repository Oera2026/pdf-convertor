using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PDFile.Core.Diagnostics;

namespace PDFile.App;

/// <summary>
/// The one crash path. Writes the report to disk first, shows one modal dialog (MessageBox if that is impossible),
/// then exits non-zero. The session marker is left behind on purpose so the next launch reports the abnormal end.
/// </summary>
internal static class Fatal
{
    private static int _handling;
    private static volatile Window? _window;

    public static void Attach(Window window) => _window = window;

    public static void Handle(Exception ex, string origin)
    {
        if (Interlocked.Exchange(ref _handling, 1) != 0)
        {
            Log.Error($"Further failure while already handling a fatal error ({origin})", ex);
            return;
        }
        var path = CrashReport.WriteToDisk(ex, origin);
        var text = CrashReport.Build(ex, origin);
        try
        {
            var window = _window;
            if (window is null)
            {
                MessageBoxThenExit(ex, origin, path);
            }
            else if (window.DispatcherQueue.HasThreadAccess)
            {
                _ = ShowDialogThenExitAsync(window, ex, origin, path, text);
            }
            else
            {
                using var started = new ManualResetEventSlim();
                var queued = window.DispatcherQueue.TryEnqueue(() =>
                {
                    started.Set();
                    _ = ShowDialogThenExitAsync(window, ex, origin, path, text);
                });
                // The dialog exits the process; this thread just waits for that. A stuck UI thread falls back after 10 s.
                if (queued && started.Wait(TimeSpan.FromSeconds(10))) Thread.Sleep(Timeout.Infinite);
                else MessageBoxThenExit(ex, origin, path);
            }
        }
        catch (Exception e)
        {
            Log.Error("Could not show the crash dialog", e);
            MessageBoxThenExit(ex, origin, path);
        }
    }

    private static async Task ShowDialogThenExitAsync(Window window, Exception ex, string origin, string path, string text)
    {
        try
        {
            var reportBox = new TextBox
            {
                Text = text,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12,
                Height = 320,
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(reportBox, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(reportBox, ScrollBarVisibility.Auto);
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = $"{ex.GetType().Name}: {ex.Message}", TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = $"Origin: {origin}. Report saved to: {(path.Length > 0 ? path : "(could not be saved, see the full text below)")}", TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            panel.Children.Add(reportBox);
            var dialog = new ContentDialog
            {
                XamlRoot = window.Content.XamlRoot,
                Title = "PDFile hit an unexpected error and must close",
                Content = panel,
                PrimaryButtonText = "Copy full report",
                SecondaryButtonText = "Open report folder",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
            };
            dialog.Resources["ContentDialogMaxWidth"] = 960.0;
            dialog.PrimaryButtonClick += (_, a) => { a.Cancel = true; Shell.CopyText(text); };
            dialog.SecondaryButtonClick += (_, a) => { a.Cancel = true; Shell.Open(path.Length > 0 ? Path.GetDirectoryName(path)! : Log.LogDirectory); };
            await dialog.ShowAsync();
        }
        catch (Exception e)
        {
            Log.Error("Crash dialog failed", e);
            MessageBoxThenExit(ex, origin, path);
        }
        Environment.Exit(1);
    }

    private static void MessageBoxThenExit(Exception ex, string origin, string path)
    {
        try
        {
            var report = path.Length > 0 ? path : "(the report could not be saved; see the log folder)";
            _ = MessageBoxW(0, $"PDFile hit an unexpected error and must close.\n\n{ex.GetType().Name}: {ex.Message}\n\nOrigin: {origin}\nFull report: {report}", "PDFile", 0x10 | 0x40000); // ICONERROR | TOPMOST
        }
        finally
        {
            Environment.Exit(1);
        }
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);
}

/// <summary>Async/UI handlers route every exception to <see cref="Fatal"/>; nothing is swallowed.</summary>
internal static class Safe
{
    public static void Run(Action action, [CallerMemberName] string origin = "")
    {
        try { action(); }
        catch (Exception ex) { Fatal.Handle(ex, origin); }
    }

    /// <summary>Fire-and-forget for event handlers (we never use async void).</summary>
    public static void Fire(Func<Task> action, [CallerMemberName] string origin = "") => _ = FireAsync(action, origin);

    private static async Task FireAsync(Func<Task> action, string origin)
    {
        try { await action(); }
        catch (Exception ex) { Fatal.Handle(ex, origin); }
    }
}
