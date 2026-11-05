using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace Otoasobi;

public sealed partial class MainPage
{
    readonly HashSet<string> _recWavNames = new(StringComparer.OrdinalIgnoreCase);
    List<RecLine> _recLines = [];
    RecAppSettings _recSettings = new();
    RecCapture? _recCapture;
    PreviewPlayer? _recPlayer;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? _recMeterTimer;
    Brush? _recAccentBrush;
    string? _recListPath;
    bool _recListNone;
    string? _recGuide;
    string? _recTakeFolder;
    string? _recTakeName;
    int _recIndex = -1;
    int _recWaitToken;
    string _recFilter = "all";
    bool _recPrefix;
    bool _recApplying = true;
    bool _recSuppress;
    bool _recAction;
    bool _recWaiting;
    bool _recTaking;
    float _recMeter;
    DateTime _recClipUntil;
    TaskCompletionSource<bool>? _recWait;

    void WireRec()
    {
        _recSettings = RecAppSettings.Load();
        _recCapture = new RecCapture();
        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (queue != null)
            _recPlayer = new PreviewPlayer(queue);
        _recAccentBrush = RecMeterFill.Background;
        _recApplying = true;
        foreach (var note in RecTone.Notes)
            RecNoteBox.Items.Add(note);
        RecNoteBox.SelectedItem = _recSettings.Note;
        RecVolumeSlider.Value = _recSettings.GuideVolume * 100;
        RecVolumeValue.Text = $"{(int)Math.Round(RecVolumeSlider.Value)}%";
        RecCountInSwitch.IsOn = _recSettings.CountIn;
        RecBpmBox.Value = _recSettings.Bpm;
        RecGuideWhileSwitch.IsOn = _recSettings.GuideWhileRecording;
        RecAutoSwitch.IsOn = _recSettings.AutoAdvance;
        _recApplying = false;
        if (queue != null)
        {
            _recMeterTimer = queue.CreateTimer();
            _recMeterTimer.Interval = TimeSpan.FromMilliseconds(33);
            _recMeterTimer.IsRepeating = true;
            _recMeterTimer.Tick += (_, _) => RecMeterTick();
            _recMeterTimer.Start();
        }

        PreviewKeyDown += Rec_PreviewKeyDown;
        Unloaded += (_, _) =>
        {
            RecCommitTake(false);
            _recMeterTimer?.Stop();
            _recPlayer?.Dispose();
            _recCapture?.Dispose();
            _recCapture = null;
        };
        _ = LoadRecDevicesAsync();
    }

    void RecSyncMonitor()
    {
        if (_recCapture == null)
            return;
        if (Dock.Contains("rec") && EditorView.Visibility == Visibility.Visible)
            _ = EnsureCaptureAsync();
        else if (!_recTaking && !_recWaiting)
            _recCapture.Stop();
    }

    async Task EnsureCaptureAsync()
    {
        if (_recCapture == null || !Dock.Contains("rec") || EditorView.Visibility != Visibility.Visible)
            return;
        try
        {
            await _recCapture.StartAsync(_recSettings.DeviceId);
            RecDeviceError.Text = "";
            RecDeviceError.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            RecDeviceError.Text = ex.Message;
            RecDeviceError.Visibility = Visibility.Visible;
        }

        UpdateRecButtons();
    }

    async Task LoadRecDevicesAsync()
    {
        try
        {
            var devices = await RecCapture.DevicesAsync();
            _recApplying = true;
            RecDeviceBox.Items.Clear();
            RecDeviceBox.Items.Add(new RecDeviceChoice("", "規定のデバイス"));
            foreach (var device in devices)
                RecDeviceBox.Items.Add(device);
            RecDeviceChoice? selected = null;
            foreach (var item in RecDeviceBox.Items)
            {
                if (item is RecDeviceChoice choice && choice.Id == (_recSettings.DeviceId ?? ""))
                    selected = choice;
            }

            RecDeviceBox.SelectedItem = selected ?? RecDeviceBox.Items.FirstOrDefault();
            _recApplying = false;
        }
        catch (Exception ex)
        {
            RecDeviceError.Text = ex.Message;
            RecDeviceError.Visibility = Visibility.Visible;
        }

        RecSyncMonitor();
    }

    void LoadRecFolder()
    {
        _recLines = [];
        _recIndex = -1;
        _recGuide = null;
        _recPrefix = false;
        _recListPath = null;
        _recListNone = false;
        if (_folder != null)
        {
            var stored = RecFolderStore.Load(_folder);
            _recPrefix = stored.PrefixPitch;
            _recGuide = string.IsNullOrWhiteSpace(stored.Guide) ? null : stored.Guide;
            _recListNone = RecFolderStore.ListIsCleared(stored.List);
            if (_recListNone)
                _recListPath = null;
            else if (!string.IsNullOrWhiteSpace(stored.List))
            {
                _recListPath = Path.IsPathRooted(stored.List)
                    ? stored.List
                    : Path.Combine(_folder, stored.List);
            }
            else
            {
                var native = Path.Combine(_folder, RecList.DefaultFileName);
                if (File.Exists(native))
                    _recListPath = native;
            }

            if (_recListPath != null && File.Exists(_recListPath))
            {
                if (!TryReadRecList(_recListPath))
                    _recListPath = null;
            }
            else
                _recListPath = null;
        }

        if (RecPrefixSwitch != null)
        {
            _recApplying = true;
            RecPrefixSwitch.IsOn = _recPrefix;
            _recApplying = false;
        }

        RefreshRecDisk();
        UpdateRecCaptions();
    }

    bool TryReadRecList(string path)
    {
        try
        {
            _recLines = RecList.Parse(File.ReadAllBytes(path)).ToList();
            _recIndex = _recLines.Count == 0 ? -1 : 0;
            _recListPath = path;
            _recListNone = false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice("録音リストを読めませんでした", ex.Message);
            return false;
        }
    }

    void RefreshRecDisk()
    {
        _recWavNames.Clear();
        if (_folder != null)
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(_folder, "*.wav"))
                    _recWavNames.Add(Path.GetFileName(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        RebuildRecView();
    }

    void RebuildRecView()
    {
        if (RecListView == null)
            return;
        var view = new List<RecDisplay>();
        var recordable = 0;
        var done = 0;
        var invalid = 0;
        var note = CurrentNote();
        foreach (var line in _recLines)
        {
            var wav = RecNames.WavName(line.Sing, note, _recPrefix);
            var exists = _recWavNames.Contains(wav);
            var status = RecNames.Status(line, exists);
            if (line.NameError != null)
                invalid++;
            else
            {
                recordable++;
                if (exists)
                    done++;
            }

            if (!ShowRecStatus(status))
                continue;
            var detail = line.Comment.Length == 0 ? $"{wav}　{status}" : $"{wav}　{status}　{line.Comment}";
            if (line.NameError != null)
                detail = $"{wav}　{line.NameError}";
            view.Add(new RecDisplay { Line = line, Title = line.Sing, Detail = detail });
        }

        if (RecProgress != null)
        {
            RecProgress.Text = invalid == 0
                ? $"{done} / {recordable}"
                : $"{done} / {recordable}　使えない行 {invalid}";
        }

        if (RecListEmpty != null)
            RecListEmpty.Visibility = _recLines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var lineNow = CurrentLine();
        RecDisplay? match = null;
        foreach (var item in view)
        {
            if (ReferenceEquals(item.Line, lineNow))
                match = item;
        }

        _recSuppress = true;
        RecListView.ItemsSource = view;
        RecListView.SelectedItem = match;
        if (match != null)
            RecListView.ScrollIntoView(match);
        _recSuppress = false;
        UpdateBooth();
        UpdateRecButtons();
    }

    bool ShowRecStatus(string status) => _recFilter switch
    {
        "todo" => status != RecNames.Recorded,
        "done" => status == RecNames.Recorded,
        _ => true,
    };

    void UpdateBooth()
    {
        if (RecSingText == null)
            return;
        var line = CurrentLine();
        if (line == null)
        {
            RecSingText.Text = _recLines.Count == 0
                ? (_recListPath == null ? "録音リストを開いてください" : "録音リストに行がありません")
                : "行を選んでください";
            RecCommentText.Text = "";
            RecCommentText.Visibility = Visibility.Collapsed;
            RecNextText.Text = "";
            RecFileText.Text = "";
            RecStatusText.Text = "";
            RecPrefixNote.Visibility = Visibility.Collapsed;
            return;
        }

        var wav = RecNames.WavName(line.Sing, CurrentNote(), _recPrefix);
        RecSingText.Text = line.Sing;
        RecCommentText.Text = line.Comment;
        RecCommentText.Visibility = line.Comment.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        var next = _recIndex + 1 < _recLines.Count ? _recLines[_recIndex + 1] : null;
        RecNextText.Text = next == null ? "" : "次　" + next.Sing;
        RecFileText.Text = wav;
        RecStatusText.Text = line.NameError ?? RecNames.Status(line, _recWavNames.Contains(wav));
        RecPrefixNote.Visibility = _recPrefix ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateRecCaptions()
    {
        FillRecListMenu();
        if (RecGuidePath == null)
            return;
        if (string.IsNullOrEmpty(_recGuide))
            RecGuidePath.Text = "ガイド音源は未設定です";
        else if (!Directory.Exists(_recGuide))
            RecGuidePath.Text = "ガイド音源のフォルダが見つかりません";
        else
            RecGuidePath.Text = _recGuide;
        ToolTipService.SetToolTip(RecGuidePath, _recGuide);
    }

    void UpdateRecButtons()
    {
        if (RecRecordButton == null)
            return;
        var idle = !_busy && EditorView.Visibility == Visibility.Visible && _folder != null;
        var line = CurrentLine();
        var wav = line == null ? "" : RecNames.WavName(line.Sing, CurrentNote(), _recPrefix);
        var ready = idle && line != null && RecNames.CanRecord(line, wav) && (_recCapture?.Running ?? false);
        RecRecordButton.IsEnabled = _recTaking || _recWaiting || ready;
        RecRecordLabel.Text = _recTaking ? "停止" : _recWaiting ? "待機をやめる" : "録音";
        var browsing = idle && !_recTaking && !_recWaiting;
        RecGuideButton.IsEnabled = browsing && line != null;
        RecPlayButton.IsEnabled = browsing && line != null && wav.Length > 0 && _recWavNames.Contains(wav);
        RecPrevButton.IsEnabled = browsing && _recIndex > 0;
        RecNextButton.IsEnabled = browsing && _recIndex >= 0 && _recIndex < _recLines.Count - 1;
        RecMissingButton.IsEnabled = browsing && _recLines.Count > 0;
        RecDeviceBox.IsEnabled = browsing;
        RecListBox.IsEnabled = idle;
        RecGuideFolderButton.IsEnabled = idle;
        RecClearGuideButton.IsEnabled = idle && !string.IsNullOrEmpty(_recGuide);
    }

    RecLine? CurrentLine() =>
        _recIndex >= 0 && _recIndex < _recLines.Count ? _recLines[_recIndex] : null;

    string CurrentNote() =>
        RecNoteBox?.SelectedItem as string ?? _recSettings.Note;

    void RecFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (_recApplying || sender is not RadioButton radio || radio.IsChecked != true || radio.Tag is not string tag)
            return;
        _recFilter = tag;
        RebuildRecView();
    }

    void RecPrefix_Toggled(object sender, RoutedEventArgs e)
    {
        if (_recApplying || RecPrefixSwitch == null)
            return;
        _recPrefix = RecPrefixSwitch.IsOn;
        SaveRecFolder();
        RebuildRecView();
    }

    void RecNote_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_recApplying || RecNoteBox.SelectedItem is not string note)
            return;
        _recSettings.Note = note;
        _recSettings.Save();
        RebuildRecView();
    }

    void RecVolume_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (RecVolumeValue == null)
            return;
        var percent = (int)Math.Round(RecVolumeSlider.Value);
        RecVolumeValue.Text = $"{percent}%";
        if (_recPlayer != null)
            _recPlayer.Volume = RecVolumeSlider.Value / 100;
        if (_recApplying)
            return;
        _recSettings.GuideVolume = RecVolumeSlider.Value / 100;
        _recSettings.Save();
    }

    void RecCountIn_Toggled(object sender, RoutedEventArgs e)
    {
        if (_recApplying)
            return;
        _recSettings.CountIn = RecCountInSwitch.IsOn;
        _recSettings.Save();
    }

    void RecBpm_Changed(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_recApplying || double.IsNaN(e.NewValue))
            return;
        _recSettings.Bpm = (int)Math.Clamp(Math.Round(e.NewValue), 40, 240);
        _recSettings.Save();
    }

    void RecGuideWhile_Toggled(object sender, RoutedEventArgs e)
    {
        if (_recApplying)
            return;
        _recSettings.GuideWhileRecording = RecGuideWhileSwitch.IsOn;
        _recSettings.Save();
    }

    void RecAuto_Toggled(object sender, RoutedEventArgs e)
    {
        if (_recApplying)
            return;
        _recSettings.AutoAdvance = RecAutoSwitch.IsOn;
        _recSettings.Save();
    }

    void RecDevice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_recApplying || RecDeviceBox.SelectedItem is not RecDeviceChoice choice)
            return;
        _recSettings.DeviceId = string.IsNullOrEmpty(choice.Id) ? null : choice.Id;
        _recSettings.Save();
        if (!_recTaking && !_recWaiting)
            _ = EnsureCaptureAsync();
    }

    void RecList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_recSuppress || RecListView.SelectedItem is not RecDisplay display)
            return;
        for (var i = 0; i < _recLines.Count; i++)
        {
            if (!ReferenceEquals(_recLines[i], display.Line))
                continue;
            _recIndex = i;
            break;
        }

        UpdateBooth();
        UpdateRecButtons();
    }

    void RecListBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_recApplying || RecListBox.SelectedItem is not RecListChoice choice)
            return;
        if (choice.Browse)
        {
            _ = RecOpenListAsync();
            return;
        }

        if (choice.Path == null)
        {
            ClearRecList();
            return;
        }

        if (SameRecList(choice.Path))
            return;
        UseRecList(choice.Path);
    }

    void RecGuideFolder_Click(object sender, RoutedEventArgs e) => _ = RecPickGuideAsync();

    void RecClearGuide_Click(object sender, RoutedEventArgs e)
    {
        _recGuide = null;
        SaveRecFolder();
        UpdateRecCaptions();
        UpdateRecButtons();
    }

    void RecGuide_Click(object sender, RoutedEventArgs e) => _ = PlayGuideAsync(loop: false);

    void RecPlay_Click(object sender, RoutedEventArgs e) => _ = PlayTakeAsync();

    void RecPrev_Click(object sender, RoutedEventArgs e) => StepRec(-1);

    void RecNext_Click(object sender, RoutedEventArgs e) => StepRec(1);

    void RecMissing_Click(object sender, RoutedEventArgs e) => SelectNextMissing();

    void RecRecord_Click(object sender, RoutedEventArgs e) => _ = RecRecordAsync();

    void ClearRecList()
    {
        _recLines = [];
        _recIndex = -1;
        _recListPath = null;
        _recListNone = true;
        SaveRecFolder();
        RefreshRecDisk();
        UpdateRecCaptions();
    }

    void UseRecList(string path)
    {
        if (!TryReadRecList(path))
        {
            FillRecListMenu();
            return;
        }

        RecListHistory.Remember(path);
        SaveRecFolder();
        RefreshRecDisk();
        UpdateRecCaptions();
    }

    bool SameRecList(string path)
    {
        if (string.IsNullOrEmpty(_recListPath))
            return false;
        try
        {
            return string.Equals(Path.GetFullPath(_recListPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    void FillRecListMenu()
    {
        if (RecListBox == null)
            return;
        var paths = RecListHistory.Load().ToList();
        if (!string.IsNullOrEmpty(_recListPath))
        {
            try
            {
                var full = Path.GetFullPath(_recListPath);
                var known = false;
                foreach (var item in paths)
                {
                    if (!string.Equals(item, full, StringComparison.OrdinalIgnoreCase))
                        continue;
                    known = true;
                    break;
                }

                if (!known)
                    paths.Insert(0, full);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }

        _recApplying = true;
        RecListBox.Items.Clear();
        RecListBox.Items.Add(new RecListChoice("選択しない", null));
        RecListBox.Items.Add(new RecListChoice("ファイルから選ぶ", null, browse: true));
        RecListChoice selected = (RecListChoice)RecListBox.Items[0];
        foreach (var path in paths)
        {
            var choice = new RecListChoice(RecListHistory.Caption(path, paths), path);
            RecListBox.Items.Add(choice);
            if (SameRecList(path))
                selected = choice;
        }

        RecListBox.SelectedItem = selected;
        ToolTipService.SetToolTip(RecListBox, _recListPath);
        _recApplying = false;
    }

    async Task RecOpenListAsync()
    {
        if (_folder == null || App.MainWindow == null)
        {
            FillRecListMenu();
            return;
        }

        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        picker.FileTypeFilter.Add(".txt");
        var file = await picker.PickSingleFileAsync();
        if (file == null || _folder == null)
        {
            FillRecListMenu();
            return;
        }

        UseRecList(file.Path);
    }

    async Task RecPickGuideAsync()
    {
        if (_folder == null || App.MainWindow == null)
            return;
        var path = await WavPicker.PickAsync(WindowNative.GetWindowHandle(App.MainWindow));
        if (path == null || _folder == null)
            return;
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(_folder), StringComparison.OrdinalIgnoreCase))
        {
            ShowNotice("ガイド音源は別のフォルダです", "録音先と同じフォルダは選べません。");
            return;
        }

        _recGuide = path;
        SaveRecFolder();
        UpdateRecCaptions();
        UpdateRecButtons();
    }

    void StepRec(int delta)
    {
        if (_recLines.Count == 0)
            return;
        _recIndex = _recIndex < 0 ? 0 : Math.Clamp(_recIndex + delta, 0, _recLines.Count - 1);
        RevealRec();
    }

    void SelectNextMissing()
    {
        if (_recLines.Count == 0)
            return;
        var start = Math.Max(_recIndex, 0);
        for (var step = 1; step <= _recLines.Count; step++)
        {
            var index = (start + step) % _recLines.Count;
            var line = _recLines[index];
            var wav = RecNames.WavName(line.Sing, CurrentNote(), _recPrefix);
            if (RecNames.Status(line, _recWavNames.Contains(wav)) != RecNames.Missing)
                continue;
            _recIndex = index;
            RevealRec();
            return;
        }
    }

    void RevealRec()
    {
        UpdateBooth();
        if (RecListView.ItemsSource is IEnumerable<RecDisplay> items)
        {
            var line = CurrentLine();
            RecDisplay? match = null;
            foreach (var item in items)
            {
                if (ReferenceEquals(item.Line, line))
                    match = item;
            }

            _recSuppress = true;
            RecListView.SelectedItem = match;
            if (match != null)
                RecListView.ScrollIntoView(match);
            _recSuppress = false;
        }

        UpdateRecButtons();
    }

    async Task RecRecordAsync()
    {
        if (_recTaking)
        {
            RecCommitTake(true);
            return;
        }

        if (_recWaiting)
        {
            CancelRecWait();
            UpdateRecButtons();
            return;
        }

        if (_recAction || _busy || _folder == null || _recCapture == null || !_recCapture.Running)
            return;
        var line = CurrentLine();
        if (line == null)
            return;
        var wavName = RecNames.WavName(line.Sing, CurrentNote(), _recPrefix);
        if (!RecNames.CanRecord(line, wavName))
            return;
        var path = Path.Combine(_folder, wavName);
        if (File.Exists(path))
        {
            if (XamlRoot == null)
                return;
            var dialog = new ContentDialog
            {
                Title = "録音を上書きしますか？",
                Content = $"{wavName} は既にあります。",
                PrimaryButtonText = "上書きする",
                CloseButtonText = "キャンセル",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || _folder == null || !ReferenceEquals(CurrentLine(), line))
                return;
        }

        _recAction = true;
        try
        {
            if (_recSettings.CountIn)
            {
                _recWaiting = true;
                UpdateRecButtons();
                var clicks = PreviewWav.Encode(RecTone.CountIn(_recSettings.Bpm, RecTone.TargetRate), 1, RecTone.TargetRate);
                var played = await PlayAndWaitAsync(clicks);
                _recWaiting = false;
                if (!played || _folder == null || !ReferenceEquals(CurrentLine(), line) || _recCapture == null)
                {
                    UpdateRecButtons();
                    return;
                }
            }

            _recTakeFolder = _folder;
            _recTakeName = wavName;
            _recCapture.BeginTake();
            _recTaking = true;
            UpdateRecButtons();
            if (_recSettings.GuideWhileRecording)
                _ = PlayGuideAsync(loop: true);
        }
        finally
        {
            _recAction = false;
        }
    }

    void RecCommitTake(bool advance)
    {
        if (!_recTaking || _recCapture == null)
            return;
        var folder = _recTakeFolder;
        var name = _recTakeName;
        _recTaking = false;
        _recPlayer?.Stop();
        var (samples, channels, rate) = _recCapture.EndTake();
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name))
        {
            UpdateRecButtons();
            return;
        }

        var mono = RecTone.ToMono44100(samples, channels, rate);
        if (mono.Length == 0)
        {
            ShowNotice("音が入りませんでした", "マイクの入力を確認して、もう一度録音してください。");
            UpdateRecButtons();
            return;
        }

        try
        {
            WavAudio.Write(Path.Combine(folder, name), new WavAudio
            {
                SampleRate = RecTone.TargetRate,
                Channels = 1,
                BitsPerSample = 16,
                Samples = mono,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice("録音を保存できませんでした", ex.Message);
            UpdateRecButtons();
            return;
        }

        if (!string.Equals(_folder, folder, StringComparison.OrdinalIgnoreCase))
            return;
        RefreshRecDisk();
        ShowNotice("録音を保存しました", name + " を書きました。解析するとファイルリストに入ります。");
        if (advance && _recSettings.AutoAdvance)
            SelectNextMissing();
        UpdateCommands();
    }

    async Task PlayGuideAsync(bool loop)
    {
        var line = CurrentLine();
        if (line == null || _recPlayer == null || _recTaking && !loop)
            return;
        if (!loop)
            _recPlayer.Stop();
        var guide = GuideFile(line);
        byte[] wav;
        if (guide != null)
        {
            try
            {
                var info = new FileInfo(guide);
                if (info.Length > 32 * 1024 * 1024)
                {
                    ShowNotice("ガイド音源が大きすぎます", Path.GetFileName(guide));
                    return;
                }

                wav = await File.ReadAllBytesAsync(guide);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowNotice("ガイド音源を読めませんでした", ex.Message);
                return;
            }
        }
        else
        {
            if (!RecTone.TryHertz(CurrentNote(), out var hz))
                hz = 261.6255653005986;
            var seconds = loop ? 1.2 : 0.8;
            wav = PreviewWav.Encode(RecTone.Tone(hz, RecTone.TargetRate, seconds), 1, RecTone.TargetRate);
        }

        if (_recPlayer == null || (loop && !_recTaking))
            return;
        _recPlayer.Volume = _recSettings.GuideVolume;
        await _recPlayer.PlayAsync(wav, loop);
    }

    string? GuideFile(RecLine line)
    {
        if (string.IsNullOrEmpty(_recGuide))
            return null;
        var path = Path.Combine(_recGuide, line.Sing.Trim() + ".wav");
        return File.Exists(path) ? path : null;
    }

    async Task PlayTakeAsync()
    {
        var line = CurrentLine();
        if (line == null || _folder == null || _recPlayer == null || _recTaking)
            return;
        var path = Path.Combine(_folder, RecNames.WavName(line.Sing, CurrentNote(), _recPrefix));
        if (!File.Exists(path))
            return;
        try
        {
            var wav = await File.ReadAllBytesAsync(path);
            _recPlayer.Volume = 1;
            await _recPlayer.PlayAsync(wav);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice("テイクを再生できませんでした", ex.Message);
        }
    }

    async Task<bool> PlayAndWaitAsync(byte[] wav)
    {
        if (_recPlayer == null)
            return false;
        var token = ++_recWaitToken;
        var done = new TaskCompletionSource<bool>();
        _recWait = done;
        void Ended(object? sender, EventArgs e)
        {
            if (token != _recWaitToken)
                return;
            Unhook();
            done.TrySetResult(true);
        }

        void Failed(object? sender, string message)
        {
            if (token != _recWaitToken)
                return;
            Unhook();
            done.TrySetResult(false);
        }

        void Unhook()
        {
            if (_recPlayer == null)
                return;
            _recPlayer.Ended -= Ended;
            _recPlayer.Failed -= Failed;
        }

        _recPlayer.Ended += Ended;
        _recPlayer.Failed += Failed;
        _recPlayer.Volume = _recSettings.GuideVolume;
        var started = await _recPlayer.PlayAsync(wav);
        if (!started || token != _recWaitToken)
        {
            Unhook();
            return false;
        }

        return await done.Task;
    }

    void CancelRecWait()
    {
        _recWaitToken++;
        _recWaiting = false;
        _recPlayer?.Stop();
        _recWait?.TrySetResult(false);
        _recWait = null;
    }

    void SaveRecFolder()
    {
        if (_folder == null)
            return;
        string? list = null;
        if (_recListNone)
            list = "";
        else if (!string.IsNullOrEmpty(_recListPath))
        {
            var root = Path.GetFullPath(_folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(_recListPath);
            list = full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full[root.Length..] : full;
        }

        try
        {
            RecFolderStore.Save(_folder, new RecFolderFile
            {
                List = list,
                Guide = _recGuide,
                PrefixPitch = _recPrefix,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice("録音の設定を保存できませんでした", ex.Message);
        }
    }

    void RecMeter_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = RecMeterTrack.ActualWidth;
        RecMeterZone.Margin = new Thickness(width * 0.75, 0, 0, 0);
        RecMeterZone.Width = Math.Max(0, width * 0.125);
    }

    void RecMeterTick()
    {
        if (RecMeterTrack == null || RecMeterTrack.ActualWidth <= 0)
            return;
        var live = _recCapture?.Running ?? false;
        var peak = live ? _recCapture!.Peak : 0;
        _recMeter = Math.Max(peak, _recMeter * 0.85f);
        if (!live && _recMeter < 0.001f)
            _recMeter = 0;
        var db = _recMeter < 0.0001f ? -96 : 20 * Math.Log10(_recMeter);
        var width = RecMeterTrack.ActualWidth;
        var position = Math.Clamp((db + 48) / 48, 0, 1);
        RecMeterFill.Width = width * position;
        if (peak >= 0.94f)
            _recClipUntil = DateTime.UtcNow.AddSeconds(1);
        var clipped = DateTime.UtcNow < _recClipUntil;
        if (clipped)
        {
            var critical = AppearanceTheme.Brush(RecMeterTrack.ActualTheme, "SystemFillColorCriticalBrush");
            if (critical != null)
                RecMeterFill.Background = critical;
        }
        else if (_recAccentBrush != null)
        {
            RecMeterFill.Background = _recAccentBrush;
        }

        RecLevelText.Text = db <= -60 ? "" : $"{db:0.0} dB";
    }

    void Rec_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_busy || EditorView.Visibility != Visibility.Visible)
            return;
        if (FocusManager.GetFocusedElement(XamlRoot) is TextBox or NumberBox or AutoSuggestBox or PasswordBox)
            return;
        switch (e.Key)
        {
            case VirtualKey.F9:
                e.Handled = true;
                _ = RecRecordAsync();
                return;
        }

        if (_recTaking || _recWaiting)
            return;
        switch (e.Key)
        {
            case VirtualKey.F10:
                e.Handled = true;
                _ = PlayTakeAsync();
                break;
            case VirtualKey.F11:
                e.Handled = true;
                _ = PlayGuideAsync(loop: false);
                break;
            case VirtualKey.F7:
                e.Handled = true;
                StepRec(-1);
                break;
            case VirtualKey.F8:
                e.Handled = true;
                StepRec(1);
                break;
            case VirtualKey.F6:
                e.Handled = true;
                SelectNextMissing();
                break;
        }
    }
}
