using Microsoft.UI.Xaml;

namespace Otoasobi;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        MainWindow = window;
        window.Closed += (_, _) => MainPage.ShutdownAnalysis();
        window.Activate();
    }
}
