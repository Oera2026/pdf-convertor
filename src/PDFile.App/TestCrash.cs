using Microsoft.UI.Dispatching;

namespace PDFile.App;

/// <summary>PDFILE_TEST_CRASH=startup|ui|safe|task|thread forces each crash route. No UI; documented in README.</summary>
internal static class TestCrash
{
    private static string? Mode => Environment.GetEnvironmentVariable("PDFILE_TEST_CRASH");

    public static void AtStartup()
    {
        if (Mode == "startup") throw new InvalidOperationException("Test crash: PDFILE_TEST_CRASH=startup");
    }

    public static void AfterWindowShown(DispatcherQueue ui)
    {
        switch (Mode)
        {
            case "ui":
                ui.TryEnqueue(() => throw new InvalidOperationException("Test crash: PDFILE_TEST_CRASH=ui"));
                break;
            case "safe":
                Safe.Fire(async () =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("Test crash: PDFILE_TEST_CRASH=safe");
                });
                break;
            case "thread":
                new Thread(() => throw new InvalidOperationException("Test crash: PDFILE_TEST_CRASH=thread")).Start();
                break;
            case "task":
                _ = Task.Run(() => throw new InvalidOperationException("Test crash: PDFILE_TEST_CRASH=task"));
                new Thread(() =>
                {
                    Thread.Sleep(500);
#pragma warning disable S1215 // forcing a GC is the whole point of this test hook
                    GC.Collect();
                    GC.WaitForPendingFinalizers(); // unobserved task exceptions only surface when the Task is finalized
#pragma warning restore S1215
#pragma warning restore S1215
                }).Start();
                break;
        }
    }
}
