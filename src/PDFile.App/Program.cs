using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using PDFile.Core;
using PDFile.Core.Diagnostics;
using PDFile.Core.Native;
using PDFile.Office;

namespace PDFile.App;

public static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Fatal.Handle(e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject?.ToString()), "AppDomain.UnhandledException");
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                e.SetObserved();
                Fatal.Handle(e.Exception, "TaskScheduler.UnobservedTaskException");
            };
            TestCrash.AtStartup();
            WinRT.ComWrappersSupport.InitializeComWrappers();
            SessionMarker.Begin();
            OfficeConverters.RegisterWithCrashReport();
            App.Converters = NativeConverters.All.Concat(OfficeConverters.Create()).ToList();
            Application.Start(p =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _ = new App();
            });
            foreach (var c in App.Converters.OfType<IDisposable>()) c.Dispose();
            SessionMarker.End();
            return 0;
        }
        catch (Exception ex)
        {
            Fatal.Handle(ex, "Main");
            return 1;
        }
    }
}
