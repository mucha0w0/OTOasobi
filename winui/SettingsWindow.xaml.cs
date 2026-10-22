using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Otoasobi;

public sealed partial class SettingsWindow : Window
{
    const int WindowWidth = 960;
    const int WindowHeight = 640;

    static SettingsWindow? _open;
    DisplaySettings? _display;

    public static void Show()
    {
        if (_open != null)
        {
            _open.Activate();
            return;
        }

        var window = new SettingsWindow();
        _open = window;
        window.Closed += (_, _) =>
        {
            window.FlushPending();
            if (ReferenceEquals(_open, window))
                _open = null;
        };
        window.Activate();
    }

    public static void CloseIfOpen() => _open?.Close();

    internal static void ApplyThemeIfOpen()
    {
        if (_open != null)
            AppearanceTheme.ApplyTo(_open);
    }

    public SettingsWindow()
    {
        InitializeComponent();

        Title = "環境設定";
        AppWindow.Title = "環境設定";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(WindowTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 720;
            presenter.PreferredMinimumHeight = 480;
        }

        PlaceOnMainWindow();
        AppearanceTheme.ApplyTo(this);
        foreach (var section in Sections())
        {
            if (section.Content is DisplaySettings display)
                _display = display;
            SettingsNav.MenuItems.Add(new NavigationViewItem
            {
                Content = section.Title,
                Tag = section,
            });
        }

        if (SettingsNav.MenuItems.Count > 0)
            SettingsNav.SelectedItem = SettingsNav.MenuItems[0];
    }

    void PlaceOnMainWindow()
    {
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest)?.WorkArea;
        int x;
        int y;
        if (App.MainWindow is Window main)
        {
            var parent = main.AppWindow;
            x = parent.Position.X + Math.Max(0, (parent.Size.Width - WindowWidth) / 2);
            y = parent.Position.Y + Math.Max(0, (parent.Size.Height - WindowHeight) / 2);
        }
        else if (work is RectInt32 area)
        {
            x = area.X + Math.Max(0, (area.Width - WindowWidth) / 2);
            y = area.Y + Math.Max(0, (area.Height - WindowHeight) / 2);
        }
        else
        {
            x = 80;
            y = 80;
        }

        if (work is RectInt32 bounds)
        {
            if (WindowWidth < bounds.Width)
                x = Math.Clamp(x, bounds.X, bounds.X + bounds.Width - WindowWidth);
            if (WindowHeight < bounds.Height)
                y = Math.Clamp(y, bounds.Y, bounds.Y + bounds.Height - WindowHeight);
        }

        AppWindow.MoveAndResize(new RectInt32(x, y, WindowWidth, WindowHeight));
    }

    void SettingsNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: SettingsSection section })
        {
            SectionTitle.Text = section.Title;
            SectionTitle.Visibility = Visibility.Visible;
            SectionHost.Content = section.Content;
            return;
        }

        SectionTitle.Text = "";
        SectionTitle.Visibility = Visibility.Collapsed;
        SectionHost.Content = null;
    }

    void FlushPending() => _display?.Flush();

    // 見出しと右に出す画面。項目を足すときはここに返す。
    static IEnumerable<SettingsSection> Sections()
    {
        yield return new SettingsSection("表示", new DisplaySettings());
    }

    sealed class SettingsSection(string title, UIElement content)
    {
        public string Title { get; } = title;

        public UIElement Content { get; } = content;
    }
}
