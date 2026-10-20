using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Otoasobi;

public sealed partial class MainWindow : Window
{
    public const string AppDisplayTitle = "オトアソビ — OTOasobi";

    public MainWindow()
    {
        InitializeComponent();

        Title = AppDisplayTitle;
        AppWindow.Title = AppDisplayTitle;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1180;
            presenter.PreferredMinimumHeight = 680;
        }

        AppWindow.Resize(new SizeInt32(1540, 840));

        RootFrame.Navigate(typeof(MainPage));
        if (RootFrame.Content is MainPage page)
        {
            DispatcherQueue queue = DispatcherQueue;
            FileDrop.Attach(this, paths => queue.TryEnqueue(() => page.OpenFolder(paths)));
        }
    }

    public void BeginUpdateCheck()
    {
        if (RootFrame.Content is MainPage page)
            page.BeginUpdateCheck();
    }

    internal void PlaceMenuInTitleBar(MenuBar menuBar)
    {
        if (menuBar.Parent is Panel panel)
            panel.Children.Remove(menuBar);
        TitleBarLeading.Children.Add(menuBar);
    }
}
