using Microsoft.UI.Xaml;
using PDFile.Core;

namespace PDFile.App;

public partial class App : Application
{
    public static IReadOnlyList<IConverter> Converters { get; set; } = [];

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            e.Handled = true; // we take over: report, dialog, exit
            Fatal.Handle(e.Exception, "Application.UnhandledException");
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args) => Safe.Run(() =>
    {
        var window = new MainWindow(new MainViewModel(Converters));
        Fatal.Attach(window);
        window.Activate();
    });
}
