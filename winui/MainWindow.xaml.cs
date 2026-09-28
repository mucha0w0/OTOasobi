using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Otoasobi;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1024;
            presenter.PreferredMinimumHeight = 680;
        }

        AppWindow.Resize(new SizeInt32(1280, 840));

        RootFrame.Navigate(typeof(MainPage));
        if (RootFrame.Content is MainPage page)
        {
            DispatcherQueue queue = DispatcherQueue;
            FileDrop.Attach(this, paths => queue.TryEnqueue(() => page.OpenFolder(paths)));
            Activated += OnFirstActivated;
        }
    }

    void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated)
            return;
        if (RootFrame.Content is not MainPage page)
            return;
        Activated -= OnFirstActivated;
        DispatcherQueue.TryEnqueue(page.ChooseFolder);
    }
}
