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
        AppAppearance.Load();
        AppearanceTheme.ApplyAccent();
        var window = new MainWindow();
        MainWindow = window;
        window.Closed += (_, _) =>
        {
            SettingsWindow.CloseIfOpen();
            MainPage.ShutdownAnalysis();
        };
        AppearanceTheme.ApplyTo(window);
        window.Activate();
        window.BeginUpdateCheck();
    }
}
