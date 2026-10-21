using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace Otoasobi;

public sealed partial class MainPage : Page
{
    static AnalysisClient? _analysis;

    readonly List<WavFile> _files = [];
    string? _folder;
    string? _notice;
    bool _busy;
    bool _wrote;
    bool _suppressSelection;
    int _previewGen;
    int _saveGen;
    IDisposable? _previewStream;
    DispatcherQueueTimer? _viewportTimer;
    string _appliedKey = "";
    bool _configReady;
    bool _analyzed;

    // oto.py の子音部、OVERLAP_AT、ブランク 0 秒（上辺の右端からのずれ）。
    // オフセットの 0 は子音開始。スライダーは ±0.1 秒で、マイナスは手前。
    // オーバーラップは子音ごと。0% が子音開始、100% が母音開始。中央が 50%。
    // 0% で左へ引いて離すと、その行だけ秒のスライダーになる。引いているあいだ値は変えない。
    const double DefaultOffsetSeconds = 0;
    const double DefaultConsonantSeconds = 0.2;
    const double DefaultBlankSeconds = 0;
    const double DefaultOverlapPercent = 50;
    const string OverlapScaleTip = "0% は子音の開始、100% は母音の開始です。0% で左へ引いて離すと、開始より前を秒で置けるスライダーになります。";

    readonly Dictionary<string, Slider> _overlapSliders = [];
    readonly Dictionary<string, Border> _overlapFrames = [];
    readonly Dictionary<string, TextBlock> _overlapValues = [];
    readonly Dictionary<string, double> _overlapIntent = [];
    Brush? _secondsFrameBrush;
    bool _overlapWrite;
    string? _overlapDragKey;
    bool _overlapArmLeft;
    readonly OtoPreset _builtIn = new()
    {
        Name = OtoPreset.BuiltInName,
        BuiltIn = true,
        Offset = DefaultOffsetSeconds,
        Consonant = DefaultConsonantSeconds,
        Blank = DefaultBlankSeconds,
    };
    readonly List<OtoPreset> _userPresets = [];
    OtoPreset? _selectedPreset;
    OtoPreset _restPreset = null!;
    bool _applyingPreset;
    bool _applyingSave;
    bool _sessionDirty;
    bool _sessionLocked;
    OtoAdjustBook _adjust = new();
    DispatcherQueueTimer? _sessionTimer;
    bool _layoutReady;
    bool _autoUpdateStarted;
    bool _paneMenuWrite;
    WavFile? _shownFile;

    public MainPage()
    {
        InitializeComponent();
        Editor.Edited += (_, _) => OnEdited();
        Editor.OtoTuned += OnOtoTuned;
        Editor.AnalyzeRequested += (_, _) => _ = StartAnalysisAsync();
        Editor.ViewportChanged += (_, _) => ScheduleViewport();
        BuildOverlapSliders();
        BuildCvvcSliders();
        OffsetSlider.ValueChanged += (_, _) => OnConfigChanged();
        ConsonantSlider.ValueChanged += (_, _) => OnConfigChanged();
        BlankSlider.ValueChanged += (_, _) => OnConfigChanged();
        RenzokuOffsetSlider.ValueChanged += (_, _) => OnConfigChanged();
        RenzokuConsonantSlider.ValueChanged += (_, _) => OnConfigChanged();
        RenzokuBlankSlider.ValueChanged += (_, _) => OnConfigChanged();
        RenzokuOverlapSlider.ValueChanged += (_, _) => OnConfigChanged();
        CvvcConsonantSlider.ValueChanged += (_, _) => OnCvvcSliderChanged();
        CvvcBlankSlider.ValueChanged += (_, _) => OnCvvcSliderChanged();
        VcOffsetSlider.ValueChanged += (_, _) => OnCvvcSliderChanged();
        VcOverlapSlider.ValueChanged += (_, _) => OnCvvcSliderChanged();
        _restPreset = _builtIn;
        _leadPreset = _builtIn;
        var queue = DispatcherQueue.GetForCurrentThread();
        if (queue != null)
        {
            _sessionTimer = queue.CreateTimer();
            _sessionTimer.IsRepeating = false;
            _sessionTimer.Interval = TimeSpan.FromMilliseconds(500);
            _sessionTimer.Tick += (_, _) => FlushSession();
        }

        LoadPresets();
        _configReady = true;
        OnConfigChanged();
        MountDock();
        WireOtoTable();
        AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnPageWheel), true);
        BindRecentFolders();
        Loaded += (_, _) => AttachMenuToTitleBar();
        Unloaded += (_, _) =>
        {
            try
            {
                FlushSession();
            }
            catch (Exception)
            {
            }
        };
        if (App.MainWindow != null)
        {
            App.MainWindow.Closed += (_, _) =>
            {
                try
                {
                    FlushSession();
                }
                catch (Exception)
                {
                }
            };
        }
    }

    void MountDock()
    {
        WaveHost.Children.Add(Editor.WaveSurface);
        SpecHost.Children.Add(Editor.SpecSurface);
        LoudHost.Children.Add(Editor.LoudSurface);
        OtoBandHost.Children.Add(Editor.OtoSurface);
        // ページが読み込まれる前は Parent が空のままなので、ここから外してからドックへ入れる。
        foreach (var pane in new UIElement[] { FilePane, WavePane, SpecHost, LoudHost, OtoBandHost, SourcePane, OtoPane, OtoParams, OtoIniPane, FrqPane })
            PaneHolder.Children.Remove(pane);
        Dock.Mount(
        [
            ("file", "ファイル", FilePane),
            ("wave", "波形", WavePane),
            ("spec", "スペクトログラム", SpecHost),
            ("loud", "音量", LoudHost),
            ("otoBand", "oto区間", OtoBandHost),
            ("source", "原音修正", SourcePane),
            ("oto", "oto推定設定", OtoPane),
            ("otoParams", "原音パラメータ", OtoParams),
            ("otoIni", "oto.ini生成", OtoIniPane),
            ("frq", ".frq生成", FrqPane),
        ]);
        Dock.PaneResizing += (_, dragging) =>
        {
            Editor.SetColumnDrag(dragging);
            Effects.SetColumnDrag(dragging);
        };
        Dock.LayoutChanged += (_, _) =>
        {
            SyncPaneMenu();
            if (!_applyingLayoutTemplate)
                _appliedLayoutTemplate = null;
            SyncLayoutTemplateMenu();
            if (_layoutReady)
                EditorLayoutStore.Save(Dock.Root, Dock.KeepRatios);
        };
        LoadLayoutTemplates();
        if (!EditorLayoutStore.TryLoad(out var layout, out var keepRatios))
            layout = EditorLayout.CreateDefault();
        _appliedLayoutTemplate = MatchLayoutTemplate(layout, keepRatios);
        Dock.Load(layout, keepRatios);
        SyncPaneMenu();
        SyncLayoutTemplateMenu();
        _layoutReady = true;
    }

    void SyncPaneMenu()
    {
        _paneMenuWrite = true;
        ShowFileItem.IsChecked = Dock.Contains("file");
        ShowWaveItem.IsChecked = Dock.Contains("wave");
        ShowSpecItem.IsChecked = Dock.Contains("spec");
        ShowLoudItem.IsChecked = Dock.Contains("loud");
        ShowOtoBandItem.IsChecked = Dock.Contains("otoBand");
        ShowSourceItem.IsChecked = Dock.Contains("source");
        ShowOtoItem.IsChecked = Dock.Contains("oto");
        ShowOtoParamsItem.IsChecked = Dock.Contains("otoParams");
        ShowOtoIniItem.IsChecked = Dock.Contains("otoIni");
        ShowFrqItem.IsChecked = Dock.Contains("frq");
        _paneMenuWrite = false;
    }

    void PaneMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_paneMenuWrite || sender is not ToggleMenuFlyoutItem item || item.Tag is not string id)
            return;
        if (item.IsChecked)
            Dock.Show(id);
        else
            Dock.Hide(id);
    }

    void ResetLayout_Click(object sender, RoutedEventArgs e) => ApplyLayoutTemplate(_builtInLayout);

    void AttachMenuToTitleBar()
    {
        if (App.MainWindow is not MainWindow window)
            return;
        window.PlaceMenuInTitleBar(AppMenuBar);
        AppMenuBar.Visibility = Visibility.Visible;
    }

    void OtoMode_Checked(object sender, RoutedEventArgs e)
    {
        if (StandaloneOtoContent == null || RenzokuOtoContent == null || CvvcOtoContent == null)
            return;
        StandaloneOtoContent.Visibility = OtoModeStandalone.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        RenzokuOtoContent.Visibility = OtoModeRenzoku.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        CvvcOtoContent.Visibility = OtoModeCvvc.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_configReady && !_applyingSave)
            OnConfigChanged();
    }

    void BuildOverlapSliders()
    {
        var valueStyle = (Style)Resources["OverlapValueStyle"];
        var groupStyle = (Style)Resources["OverlapGroupStyle"];
        string? group = null;
        StackPanel? rows = null;
        foreach (var (name, key, caption) in ConsonantOverlap.Kinds)
        {
            if (name != group)
            {
                group = name;
                rows = new StackPanel { Spacing = 8 };
                var section = new StackPanel { Spacing = 8 };
                section.Children.Add(new TextBlock { Text = name, Style = groupStyle });
                section.Children.Add(rows);
                OverlapList.Children.Add(section);
            }

            var value = new TextBlock { Text = FormatPercent(DefaultOverlapPercent), Style = valueStyle };
            var head = new Grid();
            head.Children.Add(new TextBlock { Text = caption, VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(value);
            var slider = new Slider
            {
                Maximum = 100,
                Minimum = 0,
                StepFrequency = 1,
                SmallChange = 1,
                SnapsTo = SliderSnapsTo.StepValues,
                Tag = key,
                Value = DefaultOverlapPercent,
            };
            AutomationProperties.SetName(slider, "オーバーラップ " + caption);
            ToolTipService.SetToolTip(slider, OverlapScaleTip);
            var frame = new Border
            {
                Child = slider,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 0, 4, 0),
            };
            _overlapSliders[key] = slider;
            _overlapFrames[key] = frame;
            _overlapValues[key] = value;
            _overlapIntent[key] = DefaultOverlapPercent;
            slider.ValueChanged += (_, _) => OnOverlapChanged(key, slider);
            slider.PreviewKeyDown += OnOverlapKey;
            slider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnOverlapPointerPressed), true);
            slider.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnOverlapPointerMoved), true);
            slider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnOverlapPointerEnded), true);
            slider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnOverlapPointerEnded), true);
            slider.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnOverlapPointerEnded), true);
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(head);
            row.Children.Add(frame);
            rows!.Children.Add(row);
        }
    }

    void OnOverlapChanged(string key, Slider slider)
    {
        if (_overlapWrite || _applyingPreset)
            return;
        if (slider.Minimum < 0)
        {
            var seconds = Math.Round(slider.Value, 3, MidpointRounding.AwayFromZero);
            if (seconds > -0.0005)
            {
                _overlapIntent[key] = 0;
                if (_overlapDragKey != key)
                    ShowOverlapSlider(slider, 0);
                OnConfigChanged();
                return;
            }

            _overlapIntent[key] = Math.Max(seconds, -ConsonantOverlap.MaxBeforeSeconds);
            OnConfigChanged();
            return;
        }

        _overlapIntent[key] = Math.Clamp(Math.Round(slider.Value, MidpointRounding.AwayFromZero), 0, 100);
        OnConfigChanged();
    }

    void OnOverlapPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Slider slider || slider.Tag is not string key)
            return;
        _overlapDragKey = key;
        _overlapArmLeft = false;
    }

    void OnOverlapPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Slider slider || slider.Tag is not string key || _overlapDragKey != key)
            return;
        if (slider.Minimum < 0)
            return;
        var intent = _overlapIntent.TryGetValue(key, out var stored) ? stored : DefaultOverlapPercent;
        if (intent > 0 || slider.Value > 0.5)
        {
            _overlapArmLeft = false;
            return;
        }

        var x = e.GetCurrentPoint(slider).Position.X;
        if (x < -2)
            _overlapArmLeft = true;
        else if (x > 8)
            _overlapArmLeft = false;
    }

    void OnOverlapPointerEnded(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Slider slider || slider.Tag is not string key || _overlapDragKey != key)
            return;
        _overlapDragKey = null;
        var armed = _overlapArmLeft;
        _overlapArmLeft = false;
        var intent = _overlapIntent.TryGetValue(key, out var stored) ? stored : DefaultOverlapPercent;
        var secondsMode = slider.Minimum < 0;
        if (!secondsMode && armed && intent <= 0.0005)
        {
            _overlapIntent[key] = 0;
            ShowOverlapSeconds(slider, 0);
            OnConfigChanged();
        }
        else if (secondsMode && intent >= -0.0005)
        {
            _overlapIntent[key] = 0;
            ShowOverlapSlider(slider, 0);
            OnConfigChanged();
        }
    }

    void OnOverlapKey(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not Slider slider || slider.Tag is not string key)
            return;
        var page = e.Key is Windows.System.VirtualKey.PageUp or Windows.System.VirtualKey.PageDown;
        var direction = e.Key switch
        {
            Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Down or Windows.System.VirtualKey.PageDown => -1,
            Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Up or Windows.System.VirtualKey.PageUp => 1,
            _ => 0,
        };
        if (direction == 0)
            return;
        e.Handled = true;
        var intent = _overlapIntent.TryGetValue(key, out var stored) ? stored : DefaultOverlapPercent;
        SetOverlapIntent(key, slider, StepOverlap(intent, direction, page ? 10 : 1));
    }

    void SetOverlapIntent(string key, Slider slider, double intent)
    {
        _overlapIntent[key] = intent;
        ShowOverlapSlider(slider, intent);
        OnConfigChanged();
    }

    void RepositionOverlapSliders()
    {
        foreach (var (key, slider) in _overlapSliders)
        {
            var intent = _overlapIntent.TryGetValue(key, out var stored) ? stored : DefaultOverlapPercent;
            ShowOverlapSlider(slider, intent);
        }
    }

    void ShowOverlapSlider(Slider slider, double intent)
    {
        if (intent < 0)
            ShowOverlapSeconds(slider, intent);
        else
            ShowOverlapPercent(slider, intent);
    }

    void ShowOverlapSeconds(Slider slider, double seconds)
    {
        var value = Math.Clamp(seconds, -ConsonantOverlap.MaxBeforeSeconds, 0);
        var switching = slider.Minimum >= 0;
        if (switching)
        {
            _overlapWrite = true;
            slider.Minimum = -ConsonantOverlap.MaxBeforeSeconds;
            slider.Maximum = 0;
            slider.StepFrequency = 0.001;
            slider.SmallChange = 0.001;
            slider.SnapsTo = SliderSnapsTo.StepValues;
            slider.Value = value;
            _overlapWrite = false;
            TintOverlap(slider, seconds: true);
            return;
        }

        if (Math.Abs(slider.Value - value) > 0.0001)
        {
            _overlapWrite = true;
            slider.Value = value;
            _overlapWrite = false;
        }
    }

    void ShowOverlapPercent(Slider slider, double percent)
    {
        var value = Math.Clamp(Math.Round(percent, MidpointRounding.AwayFromZero), 0, 100);
        var switching = slider.Minimum < 0;
        if (switching)
        {
            _overlapWrite = true;
            slider.Minimum = 0;
            slider.Maximum = 100;
            slider.StepFrequency = 1;
            slider.SmallChange = 1;
            slider.SnapsTo = SliderSnapsTo.StepValues;
            slider.Value = value;
            _overlapWrite = false;
            TintOverlap(slider, seconds: false);
            return;
        }

        if (Math.Abs(slider.Value - value) > 0.001)
        {
            _overlapWrite = true;
            slider.Value = value;
            _overlapWrite = false;
        }
    }

    void TintOverlap(Slider slider, bool seconds)
    {
        if (slider.Tag is not string key)
            return;
        if (_overlapValues.TryGetValue(key, out var caption))
        {
            if (seconds && ThemeBrush("AccentTextFillColorPrimaryBrush") is Brush accentText)
                caption.Foreground = accentText;
            else
                caption.ClearValue(TextBlock.ForegroundProperty);
        }

        if (!_overlapFrames.TryGetValue(key, out var frame))
            return;
        frame.Background = seconds ? SecondsFrameBrush() : null;
    }

    Brush? SecondsFrameBrush()
    {
        if (_secondsFrameBrush != null)
            return _secondsFrameBrush;
        if (ThemeBrush("AccentFillColorDefaultBrush") is not SolidColorBrush accent)
            return _secondsFrameBrush = ThemeBrush("AccentFillColorTertiaryBrush");
        _secondsFrameBrush = new SolidColorBrush(
            Windows.UI.Color.FromArgb(0x33, accent.Color.R, accent.Color.G, accent.Color.B));
        return _secondsFrameBrush;
    }

    static Brush? ThemeBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;

    static double StepOverlap(double intent, int direction, int steps)
    {
        if (direction > 0)
        {
            if (intent < 0)
            {
                var next = Math.Round(intent + 0.001 * steps, 3, MidpointRounding.AwayFromZero);
                return next > -0.0005 ? 0 : next;
            }

            return Math.Min(100, intent + steps);
        }

        if (intent > 0)
            return Math.Max(0, intent - steps);
        var before = Math.Round(intent - 0.001 * steps, 3, MidpointRounding.AwayFromZero);
        return Math.Max(before, -ConsonantOverlap.MaxBeforeSeconds);
    }

    void OnPageWheel(object sender, PointerRoutedEventArgs e)
    {
        if (EditorView.Visibility != Visibility.Visible)
            return;
        if (Editor.ZoomFromPointer(e))
            e.Handled = true;
    }

    public static void ShutdownAnalysis()
    {
        _analysis?.Dispose();
        _analysis = null;
    }

    public void OpenFolder(IReadOnlyList<string> paths)
    {
        if (_busy)
            return;
        var folder = FolderFrom(paths);
        if (folder == null)
        {
            ShowError("フォルダを選んでください。");
            return;
        }

        _ = OpenFolderAsync(folder);
    }

    async Task OpenFolderAsync(string folder)
    {
        if (_busy)
            return;
        List<string> names;
        try
        {
            names = ListWavNames(folder);
        }
        catch (DirectoryNotFoundException)
        {
            RecentFolders.Forget(folder);
            BindRecentFolders();
            ShowError("フォルダが見つかりません。");
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(ex.Message);
            return;
        }

        if (names.Count == 0)
        {
            ShowError("フォルダ内にWAVファイルがありません。");
            return;
        }

        RecentFolders.Remember(folder);
        BindRecentFolders();

        _busy = true;
        ShowBusy(0, names.Count);
        BusyTitle.Text = "元の音声を保存しています";
        FileStatusText.Text = "original へ控えています";
        var queue = DispatcherQueue;
        try
        {
            await Task.Run(() => OriginalLibrary.Prepare(folder, (name, index, count) =>
            {
                queue.TryEnqueue(() =>
                {
                    if (!_busy)
                        return;
                    BusyTitle.Text = "音声形式を揃えています";
                    SetProgress(index, count);
                    UpdateFileStatus(name, count == 0 ? 0 : (double)index / count, "形式を揃えています");
                });
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _busy = false;
            ShowError(ex.Message);
            return;
        }

        _busy = false;
        if (HasCache(folder))
        {
            await RunAsync(folder, force: false);
            return;
        }

        ShowUnanalyzed(folder, names);
    }

    async Task ConfirmReanalyzeAsync()
    {
        if (_busy || _folder == null)
            return;
        var dialog = new ContentDialog
        {
            Title = "再解析しますか？",
            Content = "保存データを使わず、フォルダの WAV を解析し直します。いまの区間は置き換わります。",
            PrimaryButtonText = "再解析する",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _busy || _folder == null)
            return;
        await RunAsync(_folder, force: true);
    }

    async Task ConfirmReanalyzeSelectedAsync()
    {
        if (_busy || _folder == null || FileList.SelectedItem is not WavFile file)
            return;
        var dialog = new ContentDialog
        {
            Title = "選択した音声だけを再解析しますか？",
            Content = $"{file.Name} だけを解析し直します。この音声のいまの区間は置き換わります。ほかの音声はそのままです。",
            PrimaryButtonText = "再解析する",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _busy || _folder == null)
            return;
        if (FileList.SelectedItem is not WavFile selected || selected != file)
            return;
        await ReanalyzeSelectedAsync(file);
    }

    void IdleView_Click(object sender, RoutedEventArgs e)
    {
        _ = PickFolderAsync();
    }

    void RecentList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not RecentFolder item)
            return;
        OpenFolder([item.Path]);
    }

    void BindRecentFolders()
    {
        var items = RecentFolders.Load().Select(path => new RecentFolder(path)).ToList();
        RecentList.ItemsSource = items;
        RecentSection.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public void BeginUpdateCheck()
    {
        if (_autoUpdateStarted)
            return;
        _autoUpdateStarted = true;
        if (IsLoaded)
            _ = AppUpdate.OfferAsync(XamlRoot, manual: false);
        else
            Loaded += (_, _) => _ = AppUpdate.OfferAsync(XamlRoot, manual: false);
    }

    void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        _ = AppUpdate.OfferAsync(XamlRoot, manual: true);
    }

    void Exit_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.Close();
    }

    async Task PickFolderAsync()
    {
        if (_busy || App.MainWindow == null)
            return;
        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        var path = await WavPicker.PickAsync(hwnd);
        if (path != null)
            OpenFolder([path]);
    }

    void WriteOto_Click(object sender, RoutedEventArgs e)
    {
        _ = WriteAsync();
    }

    void Analyze_Click(object sender, RoutedEventArgs e)
    {
        _ = StartAnalysisAsync();
    }

    void Reanalyze_Click(object sender, RoutedEventArgs e)
    {
        if (!_analyzed)
            _ = StartAnalysisAsync();
        else
            _ = ConfirmReanalyzeAsync();
    }

    async Task StartAnalysisAsync()
    {
        if (_busy || _folder == null || _analyzed)
            return;
        var dialog = new ContentDialog
        {
            Title = "すべてのデータを解析します",
            Content = "フォルダ内の WAV をすべて解析します。",
            PrimaryButtonText = "はい",
            CloseButtonText = "いいえ",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _busy || _folder == null || _analyzed)
            return;
        await RunAsync(_folder, force: true);
    }

    void ReanalyzeSelected_Click(object sender, RoutedEventArgs e)
    {
        _ = ConfirmReanalyzeSelectedAsync();
    }

    void ApplyEffects_Click(object sender, RoutedEventArgs e)
    {
        _ = ApplyEffectsAsync();
    }

    async Task ApplyEffectsAsync()
    {
        if (_busy || _folder == null)
            return;
        if (Effects.NeedsNoiseRange)
        {
            var need = new ContentDialog
            {
                Title = "無音の範囲を選んでください",
                Content = "ノイズサプレッサーがオンです。原音から無音の範囲を選んでから、適用してください。",
                CloseButtonText = "閉じる",
                XamlRoot = XamlRoot,
            };
            await need.ShowAsync();
            return;
        }

        var applyText = "original の音声に、オンのエフェクトをかけて、フォルダ直下の音声を上書きします。そのあと解析します。";
        if (Effects.NoiseApplyNote is string note)
            applyText += "\n" + note;
        var dialog = new ContentDialog
        {
            Title = "解析データはリセットされますがよろしいですか？",
            Content = applyText,
            PrimaryButtonText = "はい",
            CloseButtonText = "いいえ",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _busy || _folder == null)
            return;

        var folder = _folder;
        Effects.StopListening();
        _busy = true;
        UpdateCommands();
        ShowBusy(0, 1);
        BusyTitle.Text = "エフェクトを適用しています";
        var queue = DispatcherQueue;
        try
        {
            await Task.Run(() => OriginalLibrary.Apply(folder, Effects.Chain, (name, index, count) =>
            {
                queue.TryEnqueue(() =>
                {
                    if (_busy && _folder == folder)
                        UpdateFileStatus(name, count == 0 ? 0 : (double)index / count, "適用しています");
                });
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _busy = false;
            if (_folder != folder)
                return;
            ShowOnly("editor");
            HideStatus();
            ShowNotice("適用できませんでした", ex.Message);
            return;
        }

        if (_folder != folder)
        {
            _busy = false;
            return;
        }

        var cache = Path.Combine(folder, OtoSaveStore.CacheFileName);
        if (File.Exists(cache))
        {
            try
            {
                File.Delete(cache);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 消せなくても、このあと force で解析し直す。
            }
        }

        await RunAsync(folder, force: true);
    }

    void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection)
            return;
        if (!_revealNav)
            _revealFile = null;
        UpdateCommands();
        if (FileList.SelectedItem is not WavFile file)
            return;
        // パネル移動の配置で同じファイルの選択がもう一度来ると、波形を消して読み直してしまう。
        if (ReferenceEquals(file, _shownFile) && Editor.HasContent)
            return;
        _ = LoadPreviewAsync(file);
    }

    async Task RunAsync(string folderPath, bool force)
    {
        _busy = true;
        _wrote = false;
        AdoptFolder(folderPath);
        Effects.SetFolder(folderPath);
        _files.Clear();
        _notice = null;
        ClearLoudness();
        var total = CountTargets(folderPath);
        ShowBusy(0, total);
        var queue = DispatcherQueue;
        try
        {
            _analysis ??= new AnalysisClient();
            var result = await _analysis.AnalyzeFolderAsync(folderPath, (done, count) =>
            {
                queue.TryEnqueue(() =>
                {
                    if (_busy)
                        UpdateStatus(done, count);
                });
            }, force, (name, done, label) =>
            {
                queue.TryEnqueue(() =>
                {
                    if (_busy)
                        UpdateFileStatus(name, done, label);
                });
            });
            if (!result.Ok || result.Files.Count == 0)
            {
                var message = string.IsNullOrEmpty(result.Error) ? "解析に失敗しました。" : result.Error;
                if (RestoreUnanalyzed(folderPath, message))
                    return;
                ShowError(message);
                return;
            }

            _notice = result.Notice;
            _files.AddRange(result.Files);
            ShowEditor(folderPath, result.Cached);
        }
        catch (Exception ex)
        {
            if (!RestoreUnanalyzed(folderPath, ex.Message))
                ShowError(ex.Message);
        }
        finally
        {
            _busy = false;
            UpdateCommands();
        }
    }

    async Task ReanalyzeSelectedAsync(WavFile file)
    {
        var folder = _folder;
        if (folder == null)
            return;
        _busy = true;
        Editor.IsEnabled = false;
        ConfigBody.IsEnabled = false;
        ShowBusy(0, 1);
        var queue = DispatcherQueue;
        try
        {
            _analysis ??= new AnalysisClient();
            var result = await _analysis.AnalyzeFileAsync(folder, file.Name, (done, count) =>
            {
                queue.TryEnqueue(() =>
                {
                    if (_busy)
                        UpdateStatus(done, count);
                });
            }, (name, done, label) =>
            {
                queue.TryEnqueue(() =>
                {
                    if (_busy)
                        UpdateFileStatus(name, done, label);
                });
            });
            if (folder != _folder)
                return;
            if (!result.Ok || result.Files.Count == 0)
            {
                ShowOnly("editor");
                HideStatus();
                ShowNotice(
                    "再解析できませんでした",
                    string.IsNullOrEmpty(result.Error) ? $"{file.Name} を解析できませんでした。" : result.Error);
                return;
            }

            var updated = result.Files[0];
            Editor.Clear();
            file.Duration = updated.Duration;
            file.Engine = updated.Engine;
            file.Spans.Clear();
            file.Spans.AddRange(updated.Spans);
            RefreshOtoTable();
            var saved = await SaveCacheAsync();
            if (folder != _folder)
                return;
            ShowOnly("editor");
            HideStatus();
            if (saved)
            {
                ShowNotice(
                    "選択した音声を再解析しました",
                    $"{file.Name} の区間を置き換えました。ほかの音声はそのままです。");
            }

            if (FileList.SelectedItem != file)
                FileList.SelectedItem = file;
            else
                await LoadPreviewAsync(file);
        }
        catch (Exception ex)
        {
            if (folder != _folder)
                return;
            ShowOnly("editor");
            HideStatus();
            ShowNotice("再解析できませんでした", ex.Message);
        }
        finally
        {
            _busy = false;
            Editor.IsEnabled = true;
            ConfigBody.IsEnabled = true;
            UpdateCommands();
        }
    }

    void ScheduleViewport()
    {
        if (_viewportTimer == null)
        {
            _viewportTimer = DispatcherQueue.CreateTimer();
            _viewportTimer.Interval = TimeSpan.FromMilliseconds(40);
            _viewportTimer.IsRepeating = false;
            _viewportTimer.Tick += (_, _) => _ = LoadViewportAsync();
        }

        _viewportTimer.Stop();
        _viewportTimer.Start();
    }

    async Task LoadPreviewAsync(WavFile file)
    {
        if (_folder == null)
            return;
        _viewportTimer?.Stop();
        var gen = ++_previewGen;
        _appliedKey = "";
        _shownFile = file;
        var focus = -1;
        if (_revealFile != null && _revealFile.Equals(file.Name, StringComparison.OrdinalIgnoreCase))
            focus = _revealIndex;
        _tableFocus = focus;
        OtoParams.SetEmphasis(file.Name, focus);
        Editor.Clear();
        try
        {
            _analysis ??= new AnalysisClient();
            var (columns, rows) = Editor.RasterSize();
            var preview = await _analysis.PreviewAsync(
                Path.Combine(_folder, file.Name), 0, file.Duration, columns, rows);
            if (gen != _previewGen)
                return;
            if (!preview.Ok)
            {
                Editor.Clear();
                return;
            }

            var image = await DecodePngAsync(preview.Png);
            if (gen != _previewGen)
                return;
            if (file.Duration <= 0 && preview.Duration > 0)
                file.Duration = preview.Duration;
            Editor.SetFile(
                file,
                image,
                preview.SpecT0,
                preview.SpecT1,
                preview.Lo,
                preview.Hi,
                preview.Loudness,
                preview.WaveT0,
                preview.WaveT1,
                preview.Samples,
                preview.SampleRate);
            Editor.SetTunes(TunesFor(file));
            RememberLoudness(file.Name, preview.Loudness);
            RefreshOtoTable();
            TryRevealOto(file);
            var current = Editor.ViewportQuery();
            if (current.Columns == columns && current.Rows == rows
                && Math.Abs(current.T0) < 1e-6
                && Math.Abs(current.T1 - file.Duration) < 1e-4)
            {
                _appliedKey = ViewportKey(file.Name, current.T0, current.T1, current.Columns, current.Rows);
            }
        }
        catch (Exception)
        {
            if (gen == _previewGen)
                Editor.Clear();
        }
    }

    async Task LoadViewportAsync()
    {
        if (_folder == null || FileList.SelectedItem is not WavFile file || !Editor.HasContent)
            return;
        var query = Editor.ViewportQuery();
        var key = ViewportKey(file.Name, query.T0, query.T1, query.Columns, query.Rows);
        if (key == _appliedKey)
            return;
        var gen = ++_previewGen;
        try
        {
            _analysis ??= new AnalysisClient();
            var preview = await _analysis.PreviewAsync(
                Path.Combine(_folder, file.Name), query.T0, query.T1, query.Columns, query.Rows);
            if (gen != _previewGen || FileList.SelectedItem != file || !Editor.HasContent || !preview.Ok)
                return;
            var image = await DecodePngAsync(preview.Png);
            if (gen != _previewGen || FileList.SelectedItem != file || !Editor.HasContent)
                return;
            var latest = Editor.ViewportQuery();
            if (ViewportKey(file.Name, latest.T0, latest.T1, latest.Columns, latest.Rows) != key)
                return;
            Editor.ShowDetail(
                image,
                preview.SpecT0,
                preview.SpecT1,
                preview.Lo,
                preview.Hi,
                preview.WaveT0,
                preview.WaveT1,
                preview.Samples,
                preview.SampleRate);
            _appliedKey = key;
        }
        catch (Exception)
        {
            if (gen == _previewGen)
                _appliedKey = "";
        }
    }

    static string ViewportKey(string name, double t0, double t1, int columns, int rows)
    {
        var culture = CultureInfo.InvariantCulture;
        return string.Join(
            "|",
            name,
            t0.ToString("R", culture),
            t1.ToString("R", culture),
            columns.ToString(culture),
            rows.ToString(culture));
    }

    async Task WriteAsync()
    {
        if (_busy || _folder == null || _files.Count == 0)
            return;
        _busy = true;
        UpdateCommands();
        Editor.IsEnabled = false;
        ConfigBody.IsEnabled = false;
        ShowStatus("oto.iniを計算しています");
        try
        {
            _analysis ??= new AnalysisClient();
            var result = await _analysis.WriteAsync(
                _folder,
                _files,
                CurrentExport(),
                _adjust.For(CurrentMode()));
            if (!result.Ok || string.IsNullOrEmpty(result.Txt))
            {
                ShowNotice(
                    "oto.iniを生成できませんでした",
                    string.IsNullOrEmpty(result.Error) ? null : result.Error);
                return;
            }

            _wrote = true;
            if (!string.IsNullOrWhiteSpace(result.Notice))
            {
                _notice = string.IsNullOrWhiteSpace(_notice)
                    ? result.Notice
                    : _notice + "\n" + result.Notice;
            }

            ShowNotice("oto.iniを生成しました", WithSkip(null));
        }
        catch (Exception ex)
        {
            ShowNotice("oto.iniを生成できませんでした", ex.Message);
        }
        finally
        {
            _busy = false;
            Editor.IsEnabled = true;
            ConfigBody.IsEnabled = true;
            UpdateCommands();
            HideStatus();
        }
    }

    OtoExport CurrentExport()
    {
        if (OtoModeCvvc.IsChecked == true)
            return CvvcExport();
        if (OtoModeRenzoku.IsChecked == true)
        {
            return new(
                Math.Round(-RenzokuOffsetSlider.Value * 1000.0),
                Math.Round(RenzokuConsonantSlider.Value * 1000.0),
                RenzokuBlankSlider.Value,
                new Dictionary<string, double>(),
                "renzoku",
                RenzokuOverlapSlider.Value / 100.0,
                Math.Round(-_restPreset.Offset * 1000.0),
                Math.Round(_restPreset.Consonant * 1000.0),
                _restPreset.Blank,
                RestOverlap(_restPreset));
        }

        var overlap = new Dictionary<string, double>(ConsonantOverlap.Kinds.Length);
        foreach (var (_, key, _) in ConsonantOverlap.Kinds)
        {
            overlap[key] = ConsonantOverlap.ToEngine(
                _overlapIntent.TryGetValue(key, out var intent) ? intent : DefaultOverlapPercent);
        }

        return new(
            Math.Round(-OffsetSlider.Value * 1000.0),
            Math.Round(ConsonantSlider.Value * 1000.0),
            BlankSlider.Value,
            overlap);
    }

    void OnConfigChanged()
    {
        OffsetValue.Text = FormatSignedSeconds(OffsetSlider.Value);
        ConsonantValue.Text = FormatSeconds(ConsonantSlider.Value);
        BlankValue.Text = FormatSignedSeconds(BlankSlider.Value);
        foreach (var (key, value) in _overlapValues)
        {
            var intent = _overlapIntent.TryGetValue(key, out var stored) ? stored : DefaultOverlapPercent;
            var secondsMode = _overlapSliders.TryGetValue(key, out var slider) && slider.Minimum < 0;
            value.Text = secondsMode
                ? FormatSignedSeconds(intent < 0 ? intent : 0)
                : FormatPercent(intent);
            value.Opacity = 1;
            ToolTipService.SetToolTip(value, null);
        }
        RenzokuOffsetValue.Text = FormatSignedSeconds(RenzokuOffsetSlider.Value);
        RenzokuConsonantValue.Text = FormatSeconds(RenzokuConsonantSlider.Value);
        RenzokuBlankValue.Text = FormatSignedSeconds(RenzokuBlankSlider.Value);
        RenzokuOverlapValue.Text = FormatPercent(RenzokuOverlapSlider.Value);
        UpdateCvvcTexts();
        if (!_configReady)
            return;
        Editor.SetOtoSettings(CurrentExport());
        PushTunes();
        UpdatePresetDirty();
        UpdateCvvcPresetDirty();
        ScheduleSession();
        ScheduleOtoTable();
        if (!_wrote)
            return;
        _wrote = false;
        ShowNotice(
            "修正はまだファイルに入っていません",
            WithSkip("もう一度「oto.iniを生成」を押すと、oto.ini に反映されます。"));
    }

    void LoadPresets()
    {
        _userPresets.Clear();
        _userPresets.AddRange(OtoPresetStore.Load());
        RebuildPresetMenu();
        SelectPreset(_builtIn);
        LoadCvvcPresets();
    }

    void RebuildPresetMenu()
    {
        PresetUi.Fill(
            PresetFlyout,
            "oto-preset",
            [_builtIn],
            _userPresets,
            _selectedPreset,
            SelectPreset,
            separator: false);
        RebuildRestPresetMenu();
        RebuildCvvcLeadMenu();
    }

    void RebuildRestPresetMenu()
    {
        if (_restPreset != _builtIn && !_userPresets.Contains(_restPreset))
            _restPreset = _builtIn;
        PresetUi.Fill(
            RestPresetFlyout,
            "renzoku-rest-preset",
            [_builtIn],
            _userPresets,
            _restPreset,
            SelectRestPreset,
            separator: false);
        RestPresetNameText.Text = _restPreset.Name;
    }

    void SelectRestPreset(OtoPreset preset)
    {
        _restPreset = preset;
        RestPresetNameText.Text = preset.Name;
        foreach (var entry in RestPresetFlyout.Items)
        {
            if (entry is RadioMenuFlyoutItem radio && ReferenceEquals(radio.Tag, preset))
                radio.IsChecked = true;
        }

        if (_configReady)
            OnConfigChanged();
    }

    static Dictionary<string, double> RestOverlap(OtoPreset preset)
    {
        var overlap = new Dictionary<string, double>(ConsonantOverlap.Kinds.Length);
        foreach (var (_, key, _) in ConsonantOverlap.Kinds)
        {
            overlap[key] = ConsonantOverlap.ToEngine(
                preset.Overlap.TryGetValue(key, out var value) ? value : DefaultOverlapPercent);
        }

        return overlap;
    }

    void SelectPreset(OtoPreset preset)
    {
        _selectedPreset = preset;
        PresetNameText.Text = preset.Name;
        foreach (var entry in PresetFlyout.Items)
        {
            if (entry is RadioMenuFlyoutItem radio && ReferenceEquals(radio.Tag, preset))
                radio.IsChecked = true;
        }

        ApplyPreset(preset);
    }

    void ApplyPreset(OtoPreset preset)
    {
        _applyingPreset = true;
        foreach (var (key, _) in _overlapSliders)
        {
            _overlapIntent[key] = preset.Overlap.TryGetValue(key, out var value)
                ? ConsonantOverlap.Stored(value)
                : DefaultOverlapPercent;
        }

        OffsetSlider.Value = preset.Offset;
        ConsonantSlider.Value = preset.Consonant;
        BlankSlider.Value = preset.Blank;
        RepositionOverlapSliders();
        _applyingPreset = false;
        OnConfigChanged();
    }

    void UpdatePresetDirty()
    {
        var dirty = !_applyingPreset && _selectedPreset != null && !PresetMatches(_selectedPreset);
        PresetUi.MarkDirty(PresetFlyout, null, PresetDirtyMark, _selectedPreset, dirty, OtoPreset.BuiltInName);
    }

    bool PresetMatches(OtoPreset preset)
    {
        if (Math.Round(OffsetSlider.Value, 3) != Math.Round(preset.Offset, 3))
            return false;
        if (Math.Round(ConsonantSlider.Value, 3) != Math.Round(preset.Consonant, 3))
            return false;
        if (Math.Round(BlankSlider.Value, 3) != Math.Round(preset.Blank, 3))
            return false;
        foreach (var (key, _) in _overlapSliders)
        {
            var expected = preset.Overlap.TryGetValue(key, out var value)
                ? ConsonantOverlap.Stored(value)
                : DefaultOverlapPercent;
            var actual = _overlapIntent.TryGetValue(key, out var intent) ? intent : DefaultOverlapPercent;
            if (Math.Round(actual, 3) != Math.Round(expected, 3))
                return false;
        }

        return true;
    }

    void CopyCurrentInto(OtoPreset preset)
    {
        preset.Offset = Math.Round(OffsetSlider.Value, 3);
        preset.Consonant = Math.Round(ConsonantSlider.Value, 3);
        preset.Blank = Math.Round(BlankSlider.Value, 3);
        preset.Overlap.Clear();
        foreach (var (key, _) in _overlapSliders)
        {
            preset.Overlap[key] = ConsonantOverlap.Stored(
                _overlapIntent.TryGetValue(key, out var intent) ? intent : DefaultOverlapPercent);
        }
    }

    void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        _ = SavePresetAsync();
    }

    async Task SavePresetAsync()
    {
        var name = await PresetUi.AskNameAsync(
            XamlRoot,
            "現在の設定を保存",
            "この設定につける名前を入力してください。",
            _selectedPreset is { BuiltIn: false } ? _selectedPreset.Name : null,
            PresetNameError);
        if (name == null)
            return;

        var existing = _userPresets.FirstOrDefault(preset => preset.Name == name);
        if (existing != null && !ReferenceEquals(existing, _selectedPreset)
            && !await PresetUi.ConfirmOverwriteAsync(
                XamlRoot,
                "プリセットを上書きしますか？",
                $"「{name}」は既にあります。いまの設定で置き換えます。"))
            return;

        var target = existing ?? new OtoPreset { Name = name };
        CopyCurrentInto(target);
        if (existing == null)
            _userPresets.Add(target);
        RebuildPresetMenu();
        SelectPreset(target);
        try
        {
            OtoPresetStore.Save(_userPresets);
        }
        catch (Exception ex)
        {
            ShowNotice("プリセットを保存できませんでした", ex.Message);
        }
    }

    static string? PresetNameError(string raw) =>
        PresetUi.ReservedNameError(
            raw,
            name => name == OtoPreset.BuiltInName,
            "初期値という名前は使えません。");

    static string FormatSeconds(double value) =>
        Math.Round(value, 3).ToString("0.000", CultureInfo.InvariantCulture) + " s";

    static string FormatPercent(double value) =>
        $"{Math.Round(value).ToString(CultureInfo.InvariantCulture)}%";

    static string FormatSignedSeconds(double value)
    {
        var rounded = Math.Round(value, 3);
        var text = Math.Abs(rounded).ToString("0.000", CultureInfo.InvariantCulture);
        if (rounded > 0)
            return "+" + text + " s";
        if (rounded < 0)
            return "-" + text + " s";
        return "0.000 s";
    }

    void OnOtoTuned(object? sender, OtoAdjustedEntry entry)
    {
        if (_applyingSave)
            return;
        _adjust.Remember(CurrentMode(), entry);
        ScheduleSession();
        RefreshOtoTable();
        if (!_wrote)
            return;
        _wrote = false;
        ShowNotice(
            "修正はまだファイルに入っていません",
            WithSkip("もう一度「oto.iniを生成」を押すと、oto.ini に反映されます。"));
    }

    void PushTunes()
    {
        if (FileList.SelectedItem is not WavFile file)
        {
            Editor.SetTunes(null);
            return;
        }

        Editor.SetTunes(TunesFor(file));
    }

    List<OtoAdjustedEntry> TunesFor(WavFile file)
    {
        return _adjust.For(CurrentMode())
            .Where(entry => entry.File.Equals(file.Name, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Copy())
            .ToList();
    }

    void OnEdited()
    {
        ScheduleOtoTable();
        _ = SaveCacheAsync();
        if (!_wrote)
            return;
        _wrote = false;
        ShowNotice(
            "修正はまだファイルに入っていません",
            WithSkip("もう一度「oto.iniを生成」を押すと、oto.ini に反映されます。"));
    }

    async Task<bool> SaveCacheAsync()
    {
        var folder = _folder;
        if (folder == null || _files.Count == 0)
            return false;
        var gen = ++_saveGen;
        try
        {
            _analysis ??= new AnalysisClient();
            await _analysis.SaveCacheAsync(folder, _files);
            return true;
        }
        catch (Exception ex)
        {
            if (gen != _saveGen || folder != _folder)
                return false;
            ShowNotice("保存データを更新できませんでした", ex.Message);
            return false;
        }
    }

    async Task<BitmapImage?> DecodePngAsync(byte[] png)
    {
        if (png.Length == 0)
            return null;
        _previewStream?.Dispose();
        var stream = new InMemoryRandomAccessStream();
        _previewStream = stream;
        var writer = new DataWriter(stream);
        writer.WriteBytes(png);
        await writer.StoreAsync();
        writer.DetachStream();
        writer.Dispose();
        stream.Seek(0);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        return image;
    }

    void ShowBusy(int done, int total)
    {
        ShowOnly("busy");
        HideStatus();
        ClearNotice();
        BusyTitle.Text = "読み込んでいます";
        SetProgress(done, total);
        FileStatusText.Text = "解析するファイルを準備しています";
        FileProgress.IsIndeterminate = true;
        FileStatusBar.Visibility = Visibility.Visible;
    }

    void UpdateStatus(int done, int total)
    {
        BusyTitle.Text = "解析しています";
        SetProgress(done, total);
    }

    void SetProgress(int done, int total)
    {
        BusyCount.Text = $"{done} / {total} ファイル";
        BusyProgress.IsIndeterminate = false;
        BusyProgress.Maximum = Math.Max(total, 1);
        BusyProgress.Value = done;
    }

    void ShowUnanalyzed(string folder, IReadOnlyList<string> names)
    {
        _analyzed = false;
        _wrote = false;
        AdoptFolder(folder);
        Effects.SetFolder(folder);
        _files.Clear();
        _notice = null;
        ClearLoudness();
        foreach (var name in names)
            _files.Add(new WavFile { Name = name });
        ShowOnly("editor");
        HideStatus();
        ClearNotice();
        Editor.IsEnabled = true;
        ConfigBody.IsEnabled = true;
        Editor.SetLabelsLocked(true);
        Widen();
        _suppressSelection = true;
        FileList.ItemsSource = null;
        FileList.ItemsSource = _files;
        _suppressSelection = false;
        if (_files.Count > 0)
            FileList.SelectedIndex = 0;
        UpdateCommands();
        RefreshOtoTable();
        RefreshFrqSummary();
    }

    bool RestoreUnanalyzed(string folder, string message)
    {
        if (_analyzed)
            return false;
        var names = ListWavNames(folder);
        if (names.Count == 0)
            return false;
        ShowUnanalyzed(folder, names);
        ShowNotice("解析に失敗しました", message);
        return true;
    }

    void ShowEditor(string folder, bool cached)
    {
        _analyzed = true;
        Editor.SetLabelsLocked(false);
        Editor.IsEnabled = true;
        ConfigBody.IsEnabled = true;
        ShowOnly("editor");
        HideStatus();
        var title = cached ? "保存データから読み込みました" : "区間を確認してください";
        var body = cached
            ? "前回の区間を開きました。直すと保存データへ書き戻します。oto.ini へ反映するには「oto.iniを生成」を押してください。"
            : "位置を直したら「oto.iniを生成」を押してください。区間は OTOasibi_cache.json に保存してあり、次に開くときは再解析しません。";
        ShowNotice(title, WithSkip(body));
        Widen();
        _suppressSelection = true;
        FileList.ItemsSource = null;
        FileList.ItemsSource = _files;
        _suppressSelection = false;
        if (_files.Count > 0)
        {
            FileList.SelectedIndex = 0;
            FileList.Focus(FocusState.Programmatic);
        }

        RefreshOtoTable();
        RefreshFrqSummary();
        StartLoudness();
    }

    void ShowError(string message)
    {
        ShowOnly("error");
        HideStatus();
        ClearNotice();
        ErrorText.Text = message;
    }

    void ShowOnly(string view)
    {
        IdleView.Visibility = view == "idle" ? Visibility.Visible : Visibility.Collapsed;
        BusyView.Visibility = view == "busy" ? Visibility.Visible : Visibility.Collapsed;
        EditorView.Visibility = view == "editor" ? Visibility.Visible : Visibility.Collapsed;
        ErrorView.Visibility = view == "error" ? Visibility.Visible : Visibility.Collapsed;
        BusyRing.IsActive = view == "busy";
        UpdateCommands();
    }

    void UpdateCommands()
    {
        var idle = !_busy;
        var editing = idle && EditorView.Visibility == Visibility.Visible && _files.Count > 0;
        var ready = editing && _analyzed;
        var hasFolder = _folder != null;
        OpenMenuItem.IsEnabled = idle;
        SaveSettingsMenuItem.IsEnabled = idle && hasFolder;
        LoadSettingsMenuItem.IsEnabled = idle && hasFolder;
        WriteMenuItem.IsEnabled = ready;
        WriteButton.IsEnabled = ready;
        ReanalyzeMenuItem.Text = _analyzed ? "再解析" : "解析";
        ReanalyzeMenuItem.IsEnabled = editing;
        ReanalyzeSelectedMenuItem.IsEnabled = ready && FileList.SelectedItem is WavFile;
        ApplyEffectsButton.IsEnabled = editing;
        WriteFrqButton.IsEnabled = editing;
    }

    void UpdateFileStatus(string name, double done, string label)
    {
        var fraction = Math.Clamp(done, 0, 1);
        var percent = (int)Math.Round(fraction * 100);
        FileProgress.IsIndeterminate = false;
        FileProgress.Maximum = 1;
        FileProgress.Value = fraction;
        var detail = string.IsNullOrEmpty(label) ? $"{percent}%" : $"{label}  {percent}%";
        var text = string.IsNullOrEmpty(name) ? detail : $"{name}  {detail}";
        FileStatusText.Text = text;
        ToolTipService.SetToolTip(FileStatusText, text);
        FileStatusBar.Visibility = Visibility.Visible;
    }

    void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusProgress.IsIndeterminate = true;
        StatusBar.Visibility = Visibility.Visible;
        FileStatusBar.Visibility = Visibility.Collapsed;
    }

    void HideStatus()
    {
        StatusBar.Visibility = Visibility.Collapsed;
        StatusProgress.IsIndeterminate = false;
        FileStatusBar.Visibility = Visibility.Collapsed;
        FileProgress.IsIndeterminate = false;
    }

    void ShowNotice(string title, string? body)
    {
        var detail = string.IsNullOrWhiteSpace(body)
            ? ""
            : string.Join(" ", body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var line = string.IsNullOrEmpty(detail) ? title : title + "　" + detail;
        LogText.Text = line;
        ToolTipService.SetToolTip(LogText, line);
        LogHost.Visibility = Visibility.Visible;
    }

    void ClearNotice()
    {
        LogText.Text = "";
        ToolTipService.SetToolTip(LogText, null);
        LogHost.Visibility = Visibility.Collapsed;
    }

    string WithSkip(string? body)
    {
        if (string.IsNullOrWhiteSpace(_notice))
            return body ?? "";
        if (string.IsNullOrWhiteSpace(body))
            return _notice;
        return body + "\n" + _notice;
    }

    void Widen()
    {
        if (App.MainWindow == null)
            return;
        var size = App.MainWindow.AppWindow.Size;
        var width = Math.Max(size.Width, 1540);
        var height = Math.Max(size.Height, 840);
        if (width != size.Width || height != size.Height)
            App.MainWindow.AppWindow.Resize(new SizeInt32(width, height));
    }

    static bool HasCache(string folder) => File.Exists(Path.Combine(folder, OtoSaveStore.CacheFileName));

    static List<string> ListWavNames(string folder)
    {
        var names = new List<string>();
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                names.Add(Path.GetFileName(path));
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    static int CountTargets(string folder)
    {
        var count = 0;
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                count++;
        }

        return count;
    }

    static string? FolderFrom(IReadOnlyList<string> paths)
    {
        var folder = paths.FirstOrDefault(Directory.Exists);
        if (folder != null)
            return folder;
        var file = paths.FirstOrDefault(File.Exists);
        return file == null ? null : Path.GetDirectoryName(file);
    }
}
