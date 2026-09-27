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
        AppWindow.Resize(new SizeInt32(960, 640));

        RootFrame.Navigate(typeof(MainPage));
        if (RootFrame.Content is MainPage page)
        {
            DispatcherQueue queue = DispatcherQueue;
            FileDrop.Attach(this, paths => queue.TryEnqueue(() => page.OpenAudio(paths)));
        }
    }
}
