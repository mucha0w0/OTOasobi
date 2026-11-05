using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinRT.Interop;

namespace Otoasobi;

public sealed partial class SettingsWindow : Window
{
    const int WindowWidth = 960;
    const int WindowHeight = 640;

    static SettingsWindow? _open;
    DisplaySettings? _display;

    public static void Show()
    {
        var window = _open;
        if (window == null)
        {
            window = new SettingsWindow();
            _open = window;
            window.Closed += (_, _) =>
            {
                window.FlushPending();
                if (ReferenceEquals(_open, window))
                    _open = null;
            };
        }

        // メニューを閉じるとメインが前面に戻る。所有者にしておくと、その後も背面に回らない。
        KeepAboveMain(window);
        window.Activate();
        window.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!ReferenceEquals(_open, window))
                return;
            KeepAboveMain(window);
            window.Activate();
        });
    }

    public static void CloseIfOpen() => _open?.Close();

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

    // 最上位ウィンドウのままだと、メインのフォーカス復帰で前面を取られる。
    // GWLP_HWNDPARENT は所有者の変更で、子ウィンドウ化（SetParent）ではない。
    static void KeepAboveMain(Window window)
    {
        if (App.MainWindow is not Window main)
            return;
        var owned = WindowNative.GetWindowHandle(window);
        var owner = WindowNative.GetWindowHandle(main);
        if (owned == IntPtr.Zero || owner == IntPtr.Zero)
            return;

        var style = GetWindowLongPtr(owned, GwlExStyle);
        if ((style & WsExAppWindow) == 0)
            SetWindowLongPtr(owned, GwlExStyle, new IntPtr(style | WsExAppWindow));
        SetWindowLongPtr(owned, GwlpHwndParent, owner);
        SetWindowPos(
            owned,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    static long GetWindowLongPtr(IntPtr hwnd, int index)
    {
        if (IntPtr.Size == 8)
            return GetWindowLongPtr64(hwnd, index).ToInt64();
        return (uint)GetWindowLong32(hwnd, index);
    }

    static void SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(hwnd, index, value);
        else
            SetWindowLong32(hwnd, index, value.ToInt32());
    }

    const int GwlpHwndParent = -8;
    const int GwlExStyle = -20;
    const long WsExAppWindow = 0x00040000;
    const uint SwpNoSize = 0x0001;
    const uint SwpNoMove = 0x0002;
    const uint SwpNoZOrder = 0x0004;
    const uint SwpNoActivate = 0x0010;
    const uint SwpFrameChanged = 0x0020;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    static extern int GetWindowLong32(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

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
