using System.Globalization;
using Microsoft.UI.Dispatching;
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

    const string CacheFileName = "OTOasibi_cache.json";

    // oto.py の子音部、OVERLAP_AT、ブランク 0 秒（上辺の右端からのずれ）。
    // オフセットの 0 は子音開始。スライダーは ±0.1 秒で、マイナスは手前。
    // オーバーラップは子音ごと。0% が子音開始、100% が母音開始。
    const double DefaultOffsetSeconds = 0;
    const double DefaultConsonantSeconds = 0.2;
    const double DefaultBlankSeconds = 0;
    const double DefaultOverlapPercent = 50;

    readonly Dictionary<string, Slider> _overlapSliders = [];
    readonly Dictionary<string, TextBlock> _overlapValues = [];
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
    bool _applyingPreset;

    public MainPage()
    {
        InitializeComponent();
        Editor.Edited += (_, _) => OnEdited();
        Editor.AnalyzeRequested += (_, _) => _ = StartAnalysisAsync();
        Editor.ViewportChanged += (_, _) => ScheduleViewport();
        BuildOverlapSliders();
        OffsetSlider.ValueChanged += (_, _) => OnConfigChanged();
        ConsonantSlider.ValueChanged += (_, _) => OnConfigChanged();
        BlankSlider.ValueChanged += (_, _) => OnConfigChanged();
        LoadPresets();
        _configReady = true;
        Editor.SetOtoSettings(CurrentExport());
        AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnPageWheel), true);
        BindRecentFolders();
        Loaded += (_, _) => AttachMenuToTitleBar();
    }

    void AttachMenuToTitleBar()
    {
        if (App.MainWindow is not MainWindow window)
            return;
        window.PlaceMenuInTitleBar(AppMenuBar);
        AppMenuBar.Visibility = Visibility.Visible;
    }

    void SideTab_Checked(object sender, RoutedEventArgs e)
    {
        if (SourcePanel == null || ConfigBody == null)
            return;
        var oto = OtoTab.IsChecked == true;
        ConfigBody.Visibility = oto ? Visibility.Visible : Visibility.Collapsed;
        SourcePanel.Visibility = oto ? Visibility.Collapsed : Visibility.Visible;
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
                SnapsTo = SliderSnapsTo.StepValues,
                Value = DefaultOverlapPercent,
            };
            AutomationProperties.SetName(slider, "オーバーラップ " + caption);
            _overlapSliders[key] = slider;
            _overlapValues[key] = value;
            slider.ValueChanged += (_, _) => OnConfigChanged();
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(head);
            row.Children.Add(slider);
            rows!.Children.Add(row);
        }
    }

    void OnPageWheel(object sender, PointerRoutedEventArgs e)
    {
        if (EditorView.Visibility != Visibility.Visible || Editor.ActualWidth < 2)
            return;
        var point = e.GetCurrentPoint(Editor);
        if (point.Position.X < 0 || point.Position.Y < 0
            || point.Position.X > Editor.ActualWidth
            || point.Position.Y > Editor.ActualHeight)
            return;
        var delta = point.Properties.MouseWheelDelta;
        if (delta == 0)
            delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        if (Editor.ZoomAt(point.Position.X, delta, e))
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
        try
        {
            await Task.Run(() => OriginalLibrary.Ensure(folder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
            DefaultButton = ContentDialogButton.Close,
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
            DefaultButton = ContentDialogButton.Close,
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

    void Exit_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.Close();
    }

    void NoticeBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        NoticeHost.Visibility = Visibility.Collapsed;
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
        dialog.Opened += (_, _) => PlaceAffirmativeOnTheRight(dialog);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _busy || _folder == null || _analyzed)
            return;
        await RunAsync(_folder, force: true);
    }

    static void PlaceAffirmativeOnTheRight(DependencyObject root)
    {
        Button? yes = null;
        Button? no = null;
        foreach (var button in Descendants<Button>(root))
        {
            if (button.Content as string == "はい")
                yes = button;
            else if (button.Content as string == "いいえ")
                no = button;
        }

        if (yes == null || no == null)
            return;
        Grid.SetColumn(no, 0);
        Grid.SetColumn(yes, 4);
    }

    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
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
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        dialog.Opened += (_, _) => PlaceAffirmativeOnTheRight(dialog);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _busy || _folder == null)
            return;

        var folder = _folder;
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
            ShowNotice("適用できませんでした", ex.Message, InfoBarSeverity.Error);
            return;
        }

        if (_folder != folder)
        {
            _busy = false;
            return;
        }

        var cache = Path.Combine(folder, CacheFileName);
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
        UpdateCommands();
        if (FileList.SelectedItem is not WavFile file)
            return;
        _ = LoadPreviewAsync(file);
    }

    async Task RunAsync(string folderPath, bool force)
    {
        _busy = true;
        _folder = folderPath;
        Effects.SetFolder(folderPath);
        _files.Clear();
        _notice = null;
        _wrote = false;
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
                    string.IsNullOrEmpty(result.Error) ? $"{file.Name} を解析できませんでした。" : result.Error,
                    InfoBarSeverity.Error);
                return;
            }

            var updated = result.Files[0];
            Editor.Clear("波形を読み込んでいます…");
            file.Duration = updated.Duration;
            file.Engine = updated.Engine;
            file.Spans.Clear();
            file.Spans.AddRange(updated.Spans);
            var saved = await SaveCacheAsync();
            if (folder != _folder)
                return;
            ShowOnly("editor");
            HideStatus();
            if (saved)
            {
                ShowNotice(
                    "選択した音声を再解析しました",
                    $"{file.Name} の区間を置き換えました。ほかの音声はそのままです。",
                    InfoBarSeverity.Success);
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
            ShowNotice("再解析できませんでした", ex.Message, InfoBarSeverity.Error);
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
        ShowFile(file);
        Editor.Clear("波形を読み込んでいます…");
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
                Editor.Clear(string.IsNullOrEmpty(preview.Error) ? "波形を表示できません。" : preview.Error);
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
            var current = Editor.ViewportQuery();
            if (current.Columns == columns && current.Rows == rows
                && Math.Abs(current.T0) < 1e-6
                && Math.Abs(current.T1 - file.Duration) < 1e-4)
            {
                _appliedKey = ViewportKey(file.Name, current.T0, current.T1, current.Columns, current.Rows);
            }
        }
        catch (Exception ex)
        {
            if (gen == _previewGen)
                Editor.Clear(ex.Message);
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
            var result = await _analysis.WriteAsync(_folder, _files, CurrentExport());
            if (!result.Ok || string.IsNullOrEmpty(result.Txt))
            {
                ShowNotice(
                    "oto.iniを作成できませんでした",
                    string.IsNullOrEmpty(result.Error) ? null : result.Error,
                    InfoBarSeverity.Error);
                return;
            }

            _wrote = true;
            if (!string.IsNullOrWhiteSpace(result.Notice))
            {
                _notice = string.IsNullOrWhiteSpace(_notice)
                    ? result.Notice
                    : _notice + "\n" + result.Notice;
            }

            var severity = string.IsNullOrWhiteSpace(_notice)
                ? InfoBarSeverity.Success
                : InfoBarSeverity.Warning;
            ShowNotice("oto.iniを作成しました", WithSkip(null), severity);
        }
        catch (Exception ex)
        {
            ShowNotice("oto.iniを作成できませんでした", ex.Message, InfoBarSeverity.Error);
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
        var overlap = new Dictionary<string, double>(ConsonantOverlap.Kinds.Length);
        foreach (var (_, key, _) in ConsonantOverlap.Kinds)
        {
            overlap[key] = _overlapSliders.TryGetValue(key, out var slider)
                ? slider.Value / 100.0
                : ConsonantOverlap.Default;
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
        foreach (var (key, slider) in _overlapSliders)
            _overlapValues[key].Text = FormatPercent(slider.Value);
        if (!_configReady)
            return;
        Editor.SetOtoSettings(CurrentExport());
        UpdatePresetDirty();
        if (!_wrote)
            return;
        _wrote = false;
        ShowNotice(
            "修正はまだファイルに入っていません",
            WithSkip("もう一度「oto.iniを作成」を押すと、oto.ini に反映されます。"),
            InfoBarSeverity.Warning);
    }

    void LoadPresets()
    {
        _userPresets.Clear();
        _userPresets.AddRange(OtoPresetStore.Load());
        RebuildPresetMenu();
        SelectPreset(_builtIn);
    }

    void RebuildPresetMenu()
    {
        PresetFlyout.Items.Clear();
        AddPresetItem(_builtIn);
        foreach (var preset in _userPresets)
            AddPresetItem(preset);
    }

    void AddPresetItem(OtoPreset preset)
    {
        var item = new RadioMenuFlyoutItem
        {
            Text = preset.Name,
            GroupName = "oto-preset",
            Tag = preset,
            IsChecked = ReferenceEquals(preset, _selectedPreset),
        };
        item.Click += (_, _) => SelectPreset(preset);
        PresetFlyout.Items.Add(item);
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
        OffsetSlider.Value = preset.Offset;
        ConsonantSlider.Value = preset.Consonant;
        BlankSlider.Value = preset.Blank;
        foreach (var (key, slider) in _overlapSliders)
            slider.Value = preset.Overlap.TryGetValue(key, out var value) ? value : DefaultOverlapPercent;
        _applyingPreset = false;
        UpdatePresetDirty();
    }

    void UpdatePresetDirty()
    {
        var dirty = !_applyingPreset && _selectedPreset != null && !PresetMatches(_selectedPreset);
        PresetDirtyMark.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        foreach (var entry in PresetFlyout.Items)
        {
            if (entry is not RadioMenuFlyoutItem radio || radio.Tag is not OtoPreset preset)
                continue;
            var text = dirty && ReferenceEquals(preset, _selectedPreset) ? preset.Name + " *" : preset.Name;
            if (radio.Text != text)
                radio.Text = text;
        }
    }

    bool PresetMatches(OtoPreset preset)
    {
        if (Math.Round(OffsetSlider.Value, 3) != Math.Round(preset.Offset, 3))
            return false;
        if (Math.Round(ConsonantSlider.Value, 3) != Math.Round(preset.Consonant, 3))
            return false;
        if (Math.Round(BlankSlider.Value, 3) != Math.Round(preset.Blank, 3))
            return false;
        foreach (var (key, slider) in _overlapSliders)
        {
            var expected = preset.Overlap.TryGetValue(key, out var value) ? value : DefaultOverlapPercent;
            if (Math.Round(slider.Value) != Math.Round(expected))
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
        foreach (var (key, slider) in _overlapSliders)
            preset.Overlap[key] = Math.Round(slider.Value);
    }

    void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        _ = SavePresetAsync();
    }

    async Task SavePresetAsync()
    {
        var box = new TextBox { PlaceholderText = "プリセット名" };
        if (_selectedPreset is { BuiltIn: false })
        {
            box.Text = _selectedPreset.Name;
            box.SelectAll();
        }

        var error = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        if (Application.Current.Resources.TryGetValue("SystemFillColorCriticalBrush", out var brush) && brush is Brush paint)
            error.Foreground = paint;
        var panel = new StackPanel { Spacing = 8, MinWidth = 280 };
        panel.Children.Add(new TextBlock
        {
            Text = "この設定につける名前を入力してください。",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(box);
        panel.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = "現在の設定を保存",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        dialog.Opened += (_, _) => box.Focus(FocusState.Programmatic);
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var message = PresetNameError(box.Text);
            if (message == null)
                return;
            args.Cancel = true;
            error.Text = message;
            error.Visibility = Visibility.Visible;
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var name = box.Text.Trim();
        var existing = _userPresets.FirstOrDefault(preset => preset.Name == name);
        if (existing != null && !ReferenceEquals(existing, _selectedPreset))
        {
            var confirm = new ContentDialog
            {
                Title = "プリセットを上書きしますか？",
                Content = $"「{name}」は既にあります。いまの設定で置き換えます。",
                PrimaryButtonText = "上書きする",
                CloseButtonText = "キャンセル",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                return;
        }

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
            ShowNotice("プリセットを保存できませんでした", ex.Message, InfoBarSeverity.Error);
        }
    }

    static string? PresetNameError(string raw)
    {
        var name = raw.Trim();
        if (name.Length == 0)
            return "名前を入力してください。";
        if (name.Length > 40)
            return "名前は40文字までです。";
        if (name.Any(char.IsControl))
            return "名前に使えない文字があります。";
        if (name == OtoPreset.BuiltInName)
            return "初期値という名前は使えません。";
        return null;
    }

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

    void OnEdited()
    {
        _ = SaveCacheAsync();
        if (!_wrote)
            return;
        _wrote = false;
        ShowNotice(
            "修正はまだファイルに入っていません",
            WithSkip("もう一度「oto.iniを作成」を押すと、oto.ini に反映されます。"),
            InfoBarSeverity.Warning);
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
            ShowNotice("保存データを更新できませんでした", ex.Message, InfoBarSeverity.Error);
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
        _folder = folder;
        Effects.SetFolder(folder);
        _files.Clear();
        _notice = null;
        _wrote = false;
        foreach (var name in names)
            _files.Add(new WavFile { Name = name });
        ShowOnly("editor");
        HideStatus();
        ClearNotice();
        FolderLabel.Text = folder;
        ToolTipService.SetToolTip(FolderLabel, folder);
        FileCount.Text = $"{_files.Count} 件";
        FileTitle.Text = "";
        ToolTipService.SetToolTip(FileTitle, null);
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
    }

    bool RestoreUnanalyzed(string folder, string message)
    {
        if (_analyzed)
            return false;
        var names = ListWavNames(folder);
        if (names.Count == 0)
            return false;
        ShowUnanalyzed(folder, names);
        ShowNotice("解析に失敗しました", message, InfoBarSeverity.Error);
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
        FolderLabel.Text = folder;
        ToolTipService.SetToolTip(FolderLabel, folder);
        FileCount.Text = $"{_files.Count} 件";
        var severity = string.IsNullOrWhiteSpace(_notice)
            ? InfoBarSeverity.Informational
            : InfoBarSeverity.Warning;
        var title = cached ? "保存データから読み込みました" : "区間を確認してください";
        var body = cached
            ? "前回の区間を開きました。直すと保存データへ書き戻します。oto.ini へ反映するには「oto.iniを作成」を押してください。"
            : "位置を直したら「oto.iniを作成」を押してください。区間は OTOasibi_cache.json に保存してあり、次に開くときは再解析しません。";
        ShowNotice(title, WithSkip(body), severity);
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
        OpenMenuItem.IsEnabled = idle;
        WriteMenuItem.IsEnabled = ready;
        WriteButton.IsEnabled = ready;
        ReanalyzeMenuItem.Text = _analyzed ? "再解析" : "解析";
        ReanalyzeMenuItem.IsEnabled = editing;
        ReanalyzeSelectedMenuItem.IsEnabled = ready && FileList.SelectedItem is WavFile;
        ApplyEffectsButton.IsEnabled = editing;
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

    void ShowNotice(string title, string? body, InfoBarSeverity severity)
    {
        NoticeBar.Title = title;
        NoticeBar.Message = body ?? "";
        NoticeBar.Severity = severity;
        NoticeBar.IsOpen = true;
        NoticeHost.Visibility = Visibility.Visible;
    }

    void ClearNotice()
    {
        NoticeBar.IsOpen = false;
        NoticeHost.Visibility = Visibility.Collapsed;
    }

    string WithSkip(string? body)
    {
        if (string.IsNullOrWhiteSpace(_notice))
            return body ?? "";
        if (string.IsNullOrWhiteSpace(body))
            return _notice;
        return body + "\n" + _notice;
    }

    void ShowFile(WavFile file)
    {
        FileTitle.Text = file.Name;
        ToolTipService.SetToolTip(FileTitle, file.Name);
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

    static bool HasCache(string folder) => File.Exists(Path.Combine(folder, CacheFileName));

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
