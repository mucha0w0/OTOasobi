using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
    string? _recListPath;
    bool _recListNone;
    string? _recGuide;
    bool _recGuideManual = true;
    string? _recTakeFolder;
    string? _recTakeName;
    int _recIndex = -1;
    int _recWaitToken;
    int _guideShapeToken;
    bool _guideListening;
    string _recFilter = "all";
    bool _recApplying = true;
    bool _recSuppress;
    bool _recAction;
    bool _recWaiting;
    bool _recTaking;
    string? _recInputError;
    string? _recOutputError;
    TaskCompletionSource<bool>? _recWait;

    void WireRec()
    {
        _recSettings = RecAppSettings.Load();
        _recCapture = new RecCapture();
        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (queue != null)
        {
            _recPlayer = new PreviewPlayer(queue);
            _recPlayer.SetOutput(_recSettings.OutputDeviceId);
            _recPlayer.Failed += (_, message) => ShowNotice("再生できませんでした", message);
            _recPlayer.Ended += (_, _) => _guideListening = false;
        }
        _recApplying = true;
        foreach (var note in RecTone.Notes)
            RecNoteBox.Items.Add(note);
        RecNoteBox.SelectedItem = _recSettings.Note;
        RecVolumeSlider.Value = _recSettings.GuideVolume * 100;
        RecVolumeValue.Text = $"{(int)Math.Round(RecVolumeSlider.Value)}%";
        RecGuideSpeedSlider.Value = _recSettings.GuideSpeed * 100;
        RecGuideSpeedValue.Text = $"{(int)Math.Round(RecGuideSpeedSlider.Value)}%";
        RecGuidePitchSlider.Value = _recSettings.GuidePitch;
        RecGuidePitchValue.Text = FormatGuidePitch((int)Math.Round(RecGuidePitchSlider.Value));
        RecCountInSwitch.IsOn = _recSettings.CountIn;
        RecCountBeatsBox.Value = _recSettings.CountBeats;
        RecCountBeatsAfterBox.Value = _recSettings.CountBeatsAfter;
        RecBpmBox.Value = _recSettings.Bpm;
        RecMorasBox.Value = _recSettings.Moras;
        RecMoraBeatsBox.Value = _recSettings.MoraBeats;
        RecAutoSwitch.IsOn = _recSettings.AutoAdvance;
        _recApplying = false;

        PreviewKeyDown += Rec_PreviewKeyDown;
        RecAppSettings.DevicesChanged += OnRecDevicesChanged;
        Unloaded += (_, _) =>
        {
            RecAppSettings.DevicesChanged -= OnRecDevicesChanged;
            RecCommitTake(false);
            _micTimer?.Stop();
            _micPlayer?.Dispose();
            _recPlayer?.Dispose();
            _recCapture?.Dispose();
            _recCapture = null;
        };
        WireMic();
        _ = ApplyRecOutputAsync();
        RecSyncMonitor();
    }

    void OnRecDevicesChanged(object? sender, EventArgs e)
    {
        var fresh = RecAppSettings.Load();
        _recSettings.DeviceId = fresh.DeviceId;
        _recSettings.OutputDeviceId = fresh.OutputDeviceId;
        _recPlayer?.SetOutput(_recSettings.OutputDeviceId);
        _micPlayer?.SetOutput(_recSettings.OutputDeviceId);
        _ = ApplyRecOutputAsync();
        if (!_recTaking && !_recWaiting)
            _ = EnsureCaptureAsync();
        _ = ApplyMicListenAsync();
    }

    bool RecMonitorWanted() =>
        EditorView.Visibility == Visibility.Visible && (Dock.Contains("rec") || Dock.Contains("mic"));

    void RecSyncMonitor()
    {
        if (_recCapture == null)
            return;
        if (RecMonitorWanted())
            _ = EnsureCaptureAsync();
        else if (!_recTaking && !_recWaiting)
            _recCapture.Stop();
        MicSync();
    }

    async Task EnsureCaptureAsync()
    {
        if (_recCapture == null || !RecMonitorWanted())
            return;
        try
        {
            await _recCapture.StartAsync(_recSettings.DeviceId, _recSettings.OutputDeviceId);
            if (!RecMonitorWanted() && !_recTaking && !_recWaiting)
            {
                _recCapture.Stop();
                return;
            }

            _recInputError = null;
        }
        catch (Exception ex)
        {
            _recInputError = ex.Message;
        }

        ShowRecDeviceError();
        UpdateRecButtons();
        await ApplyMicListenAsync();
        MicSync();
    }

    async Task ApplyRecOutputAsync()
    {
        var failed = false;
        foreach (var player in new[] { _recPlayer, _micPlayer })
        {
            if (player == null)
                continue;
            try
            {
                await player.ApplyOutputAsync();
            }
            catch (Exception)
            {
                failed = true;
            }
        }

        _recOutputError = failed ? "出力デバイスを開けませんでした。" : null;
        ShowRecDeviceError();
    }

    void ShowRecDeviceError()
    {
        if (RecDeviceError == null)
            return;
        RecDeviceError.Text = _recInputError ?? "";
        RecDeviceError.Visibility = string.IsNullOrEmpty(_recInputError) ? Visibility.Collapsed : Visibility.Visible;
        if (MicDeviceError != null)
        {
            var monitor = string.Join(" ", new[] { _recInputError, _recOutputError, _micListenError }.Where(text => !string.IsNullOrEmpty(text)));
            MicDeviceError.Text = monitor;
            MicDeviceError.Visibility = monitor.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        var message = string.Join(" ", new[] { _recInputError, _recOutputError }.Where(text => !string.IsNullOrEmpty(text)));
        RecAppSettings.ReportCapture(message.Length == 0 ? null : message);
    }

    void LoadRecFolder()
    {
        _recLines = [];
        _recIndex = -1;
        _recGuide = null;
        _recGuideManual = true;
        _recListPath = null;
        _recListNone = false;
        if (_folder != null)
        {
            var stored = RecFolderStore.Load(_folder);
            _recGuideManual = RecFolderStore.UseManualGuide(stored.GuideManual, stored.Guide);
            if (RecFolderStore.IsGuideWav(stored.Guide) && stored.Guide != null)
            {
                _recGuide = stored.Guide;
                RecGuideHistory.Remember(stored.Guide);
            }
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

        RefreshRecDisk();
        ApplyGuideKind();
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
        var done = 0;
        foreach (var line in _recLines)
        {
            var wav = RecNames.WavName(line.Sing);
            var exists = _recWavNames.Contains(wav);
            var status = RecNames.Status(line, exists);
            if (line.NameError == null && exists)
                done++;
            if (_recFilter == "extra" || !ShowRecStatus(status))
                continue;
            var detail = line.Comment.Length == 0 ? $"{wav}　{status}" : $"{wav}　{status}　{line.Comment}";
            if (line.NameError != null)
                detail = $"{wav}　{line.NameError}";
            view.Add(new RecDisplay { Line = line, Title = line.Sing, Detail = detail });
        }

        if (_recFilter == "extra")
        {
            foreach (var name in RecNames.Outside(_recWavNames, _recLines))
            {
                var stem = name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
                view.Add(new RecDisplay { Title = stem, Detail = name });
            }
        }

        if (RecProgress != null)
            RecProgress.Text = $"{done} / {_recLines.Count}";

        if (RecListEmpty != null)
        {
            var showEmpty = view.Count == 0 && (_recFilter == "extra" || _recLines.Count == 0);
            RecListEmpty.Visibility = showEmpty ? Visibility.Visible : Visibility.Collapsed;
            if (showEmpty)
            {
                RecListEmpty.Text = _recFilter == "extra"
                    ? "リストに無い WAV はありません。"
                    : "リストを選ぶと、ここに行が出ます。";
            }
        }
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
            RecNextText.Visibility = Visibility.Collapsed;
            RecStatusText.Text = "";
            RecStatusHost.Visibility = Visibility.Collapsed;
            RefreshMicTake();
            return;
        }

        var wav = RecNames.WavName(line.Sing);
        RecSingText.Text = line.Sing;
        RecCommentText.Text = line.Comment;
        RecCommentText.Visibility = line.Comment.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        var next = _recIndex + 1 < _recLines.Count ? _recLines[_recIndex + 1] : null;
        RecNextText.Text = next == null ? "" : "次　" + next.Sing;
        RecNextText.Visibility = next == null ? Visibility.Collapsed : Visibility.Visible;
        RecStatusText.Text = line.NameError ?? RecNames.Status(line, _recWavNames.Contains(wav));
        RecStatusHost.Visibility = Visibility.Visible;
        RefreshMicTake();
    }

    void UpdateRecCaptions()
    {
        FillRecListMenu();
        FillRecGuideMenu();
    }

    void UpdateRecButtons()
    {
        if (RecRecordButton == null)
            return;
        var idle = !_busy && EditorView.Visibility == Visibility.Visible && _folder != null;
        var line = CurrentLine();
        var wav = line == null ? "" : RecNames.WavName(line.Sing);
        var ready = idle && line != null && RecNames.CanRecord(line, wav) && (_recCapture?.Running ?? false);
        RecRecordButton.IsEnabled = _recTaking || _recWaiting || ready;
        RecRecordLabel.Text = _recTaking ? "停止" : _recWaiting ? "待機をやめる" : "録音";
        var browsing = idle && !_recTaking && !_recWaiting;
        RecGuideButton.IsEnabled = browsing && line != null;
        var canHear = browsing && line != null && wav.Length > 0 && _recWavNames.Contains(wav);
        var hearLabel = _micPlaying ? "停止" : "テイクを聴く";
        RecPlayButton.IsEnabled = canHear || _micPlaying;
        RecPlayButton.Content = hearLabel;
        ToolTipService.SetToolTip(RecPlayButton, _micPlaying ? "停止" : "保存した WAV を再生します（F10）");
        MicListenSwitch.IsEnabled = idle && (_recCapture?.Running ?? false);
        RecPrevButton.IsEnabled = browsing && _recIndex > 0;
        RecNextButton.IsEnabled = browsing && _recIndex >= 0 && _recIndex < _recLines.Count - 1;
        RecMissingButton.IsEnabled = browsing && _recLines.Count > 0;
        RecListBox.IsEnabled = idle;
        RecGuideBox.IsEnabled = idle;
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

    void RecNote_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_recApplying || RecNoteBox.SelectedItem is not string note)
            return;
        _recSettings.Note = note;
        _recSettings.Save();
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

    void RecGuideSpeed_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (RecGuideSpeedValue == null)
            return;
        var percent = (int)Math.Round(RecGuideSpeedSlider.Value);
        RecGuideSpeedValue.Text = $"{percent}%";
        if (_recApplying)
            return;
        _recSettings.GuideSpeed = percent / 100.0;
        _recSettings.Save();
        QueueGuideReshape();
    }

    void RecGuidePitch_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (RecGuidePitchValue == null)
            return;
        var semis = (int)Math.Round(RecGuidePitchSlider.Value);
        RecGuidePitchValue.Text = FormatGuidePitch(semis);
        if (_recApplying)
            return;
        _recSettings.GuidePitch = semis;
        _recSettings.Save();
        QueueGuideReshape();
    }

    static string FormatGuidePitch(int semis) =>
        semis > 0 ? $"+{semis}" : semis.ToString();

    void QueueGuideReshape()
    {
        if (_recGuideManual || !_guideListening)
            return;
        var token = ++_guideShapeToken;
        _ = ReshapeGuideAsync(token);
    }

    async Task ReshapeGuideAsync(int token)
    {
        await Task.Delay(180);
        if (token != _guideShapeToken || _recGuideManual || !_guideListening)
            return;
        await PlayGuideAsync(loop: false);
    }

    void RecCountIn_Toggled(object sender, RoutedEventArgs e)
    {
        if (_recApplying)
            return;
        _recSettings.CountIn = RecCountInSwitch.IsOn;
        _recSettings.Save();
    }

    void RecCountBeats_Changed(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_recApplying || double.IsNaN(e.NewValue))
            return;
        _recSettings.CountBeats = (int)Math.Clamp(Math.Round(e.NewValue), RecTone.MinCountBeats, RecTone.MaxCountBeats);
        _recSettings.Save();
    }

    void RecCountBeatsAfter_Changed(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_recApplying || double.IsNaN(e.NewValue))
            return;
        _recSettings.CountBeatsAfter = (int)Math.Clamp(Math.Round(e.NewValue), 0, RecTone.MaxCountBeats);
        _recSettings.Save();
    }

    void RecBpm_Changed(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_recApplying || double.IsNaN(e.NewValue))
            return;
        _recSettings.Bpm = (int)Math.Clamp(Math.Round(e.NewValue), 40, 240);
        _recSettings.Save();
    }

    void RecMoras_Changed(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_recApplying || double.IsNaN(e.NewValue))
            return;
        _recSettings.Moras = (int)Math.Clamp(Math.Round(e.NewValue), RecTone.MinMoras, RecTone.MaxMoras);
        _recSettings.Save();
    }

    void RecMoraBeats_Changed(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_recApplying || double.IsNaN(e.NewValue))
            return;
        _recSettings.MoraBeats = Math.Clamp(e.NewValue, RecTone.MinMoraBeats, RecTone.MaxMoraBeats);
        _recSettings.Save();
    }

    void RecAuto_Toggled(object sender, RoutedEventArgs e)
    {
        if (_recApplying)
            return;
        _recSettings.AutoAdvance = RecAutoSwitch.IsOn;
        _recSettings.Save();
    }

    void RecList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_recSuppress || RecListView.SelectedItem is not RecDisplay display)
            return;
        if (display.Line == null)
            _recIndex = -1;
        else
        {
            for (var i = 0; i < _recLines.Count; i++)
            {
                if (!ReferenceEquals(_recLines[i], display.Line))
                    continue;
                _recIndex = i;
                break;
            }
        }

        UpdateBooth();
        UpdateRecButtons();
        FollowRecSelection();
    }

    void FollowRecSelection()
    {
        if (_fileSync || RecListView == null)
            return;
        if (RecListView.SelectedItem is RecDisplay display)
        {
            ShowRecWav(RecNames.WavOf(display.Line, display.Detail));
            return;
        }

        var line = CurrentLine();
        if (line != null)
            ShowRecWav(RecNames.WavName(line.Sing));
    }

    void ShowRecWav(string wav)
    {
        if (wav.Length == 0 || _folder == null)
            return;
        if (!WavOnDisk(wav))
        {
            _fileSync = true;
            FileList.SelectedItem = null;
            _fileSync = false;
            ShowMissingGraphs();
            return;
        }

        WavFile? file = null;
        foreach (var item in _files)
        {
            if (!item.Name.Equals(wav, StringComparison.OrdinalIgnoreCase))
                continue;
            file = item;
            break;
        }

        if (file != null)
        {
            var same = ReferenceEquals(FileList.SelectedItem, file);
            _fileSync = true;
            if (!same)
            {
                FileList.SelectedItem = file;
                FileList.ScrollIntoView(file);
            }

            _fileSync = false;
            if (!same || (ReferenceEquals(file, _shownFile) && Editor.HasContent))
                return;
            ShowFrqFor(file);
            _ = LoadPreviewAsync(file);
            return;
        }

        _fileSync = true;
        FileList.SelectedItem = null;
        _fileSync = false;
        var loose = new WavFile { Name = wav };
        ShowFrqFor(loose);
        _ = LoadPreviewAsync(loose);
    }

    bool WavOnDisk(string wav)
    {
        if (_folder == null || wav.Length == 0 || wav != Path.GetFileName(wav))
            return false;
        try
        {
            return File.Exists(Path.Combine(_folder, wav));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    void ShowMissingGraphs()
    {
        _viewportTimer?.Stop();
        _previewGen++;
        _appliedKey = "";
        _shownFile = null;
        ShowFrqFor(null);
        Editor.ShowMissingFile();
    }

    void SelectRecForWav(string? wav)
    {
        if (RecListView == null)
            return;
        RecDisplay? match = null;
        var index = -1;
        if (!string.IsNullOrEmpty(wav) && RecListView.ItemsSource is IEnumerable<RecDisplay> items)
        {
            index = RecNames.IndexOfWav(_recLines, wav);
            foreach (var item in items)
            {
                var name = RecNames.WavOf(item.Line, item.Detail);
                if (name.Length == 0 || !name.Equals(wav, StringComparison.OrdinalIgnoreCase))
                    continue;
                match = item;
                break;
            }
        }

        _recIndex = index;
        _recSuppress = true;
        RecListView.SelectedItem = match;
        if (match != null)
            RecListView.ScrollIntoView(match);
        _recSuppress = false;
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

        if (SameRecPath(_recListPath, choice.Path))
            return;
        UseRecList(choice.Path);
    }

    void RecGuideBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_recApplying || RecGuideBox.SelectedItem is not RecListChoice choice)
            return;
        if (choice.Browse)
        {
            _ = RecPickGuideAsync();
            return;
        }

        if (choice.Path == null)
        {
            ClearRecGuide();
            return;
        }

        if (SameRecPath(_recGuide, choice.Path))
            return;
        UseRecGuide(choice.Path);
    }

    void RecGuideKind_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs e)
    {
        if (_recApplying)
            return;
        var manual = ReferenceEquals(sender.SelectedItem, RecGuideManualItem);
        if (manual == _recGuideManual)
        {
            ApplyGuideKind();
            return;
        }

        _recGuideManual = manual;
        ApplyGuideKind();
        SaveRecFolder();
    }

    void ApplyGuideKind()
    {
        if (RecGuideFilePanel != null)
            RecGuideFilePanel.Visibility = _recGuideManual ? Visibility.Collapsed : Visibility.Visible;
        if (RecGuideManualPanel != null)
            RecGuideManualPanel.Visibility = _recGuideManual ? Visibility.Visible : Visibility.Collapsed;
        if (RecGuideKindBar == null)
            return;
        _recApplying = true;
        RecGuideKindBar.SelectedItem = _recGuideManual ? RecGuideManualItem : RecGuideFileItem;
        _recApplying = false;
    }

    void RecGuide_Click(object sender, RoutedEventArgs e) => _ = PlayGuideAsync(loop: false);

    void RecPlay_Click(object sender, RoutedEventArgs e) => _ = PlayMicTakeAsync();

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

    bool SameRecPath(string? left, string right)
    {
        if (string.IsNullOrEmpty(left))
            return false;
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    void FillRecListMenu() =>
        FillRecChoiceMenu(RecListBox, _recListPath, RecListHistory.Load(), "ファイルから選ぶ");

    void FillRecGuideMenu() =>
        FillRecChoiceMenu(RecGuideBox, _recGuide, RecGuideHistory.Load(), "ファイルから選ぶ");

    void FillRecChoiceMenu(ComboBox? box, string? current, IReadOnlyList<string> history, string browse)
    {
        if (box == null)
            return;
        var paths = history.ToList();
        if (!string.IsNullOrEmpty(current))
        {
            try
            {
                var full = Path.GetFullPath(current);
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
        box.Items.Clear();
        box.Items.Add(new RecListChoice("選択しない", null));
        box.Items.Add(new RecListChoice(browse, null, browse: true));
        var selected = (RecListChoice)box.Items[0];
        foreach (var path in paths)
        {
            var choice = new RecListChoice(RecListHistory.Caption(path, paths), path);
            box.Items.Add(choice);
            if (SameRecPath(current, path))
                selected = choice;
        }

        box.SelectedItem = selected;
        ToolTipService.SetToolTip(box, current);
        _recApplying = false;
    }

    void ClearRecGuide()
    {
        _recGuide = null;
        SaveRecFolder();
        UpdateRecCaptions();
        UpdateRecButtons();
    }

    void UseRecGuide(string path)
    {
        if (!RecFolderStore.IsGuideWav(path))
        {
            ShowNotice("ガイドは WAV を選んでください", "同じ名前の txt が対になります。");
            FillRecGuideMenu();
            return;
        }

        string? directory = null;
        try
        {
            directory = Path.GetDirectoryName(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }

        if (_folder != null && directory != null && SameRecPath(directory, _folder))
        {
            ShowNotice("ガイドは別の場所の WAV にしてください", "録音先のフォルダにあるファイルは選べません。");
            FillRecGuideMenu();
            return;
        }

        _recGuide = path;
        RecGuideHistory.Remember(path);
        SaveRecFolder();
        UpdateRecCaptions();
        UpdateRecButtons();
        var text = RecFolderStore.GuideTextPath(path);
        if (!File.Exists(text))
            ShowNotice("同じ名前の txt がありません", Path.GetFileName(text));
        else
            TryApplyGuideScript(path);
    }

    void TryApplyGuideScript(string wavPath)
    {
        double duration;
        try
        {
            var audio = WavAudio.Read(wavPath);
            duration = audio.Samples.Length / (double)Math.Max(1, audio.Channels) / audio.SampleRate;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return;
        }

        var offsets = RecGuideScript.Parse(RecFolderStore.GuideTextPath(wavPath), duration);
        if (offsets.StartAfter == null && offsets.EndBefore == null)
            return;
        if (offsets.StartAfter is double start)
            _recSettings.RecStartAfter = start;
        if (offsets.EndBefore is double end)
            _recSettings.RecEndBefore = end;
        _recSettings.Save();
        _recSettings = RecAppSettings.Load();
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
        {
            FillRecGuideMenu();
            return;
        }

        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        picker.FileTypeFilter.Add(".wav");
        var file = await picker.PickSingleFileAsync();
        if (file == null || _folder == null)
        {
            FillRecGuideMenu();
            return;
        }

        UseRecGuide(file.Path);
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
            var wav = RecNames.WavName(line.Sing);
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

        FollowRecSelection();

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
        var wavName = RecNames.WavName(line.Sing);
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
            if (_recGuideManual && _recSettings.CountIn)
            {
                _recWaiting = true;
                StopMicPlayback();
                UpdateRecButtons();
                var clicks = PreviewWav.Encode(RecTone.CountIn(_recSettings.Bpm, RecTone.TargetRate, _recSettings.CountBeats), 1, RecTone.TargetRate);
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
            BeginMicTake();
            UpdateRecButtons();
            _ = PlayGuideAsync(loop: false);
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
            EndMicTake();
            UpdateRecButtons();
            return;
        }

        var mono = RecTone.ToMono44100(samples, channels, rate);
        if (mono.Length == 0)
        {
            ShowNotice("音が入りませんでした", "マイクの入力を確認して、もう一度録音してください。");
            EndMicTake();
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
            EndMicTake();
            UpdateRecButtons();
            return;
        }

        if (!string.Equals(_folder, folder, StringComparison.OrdinalIgnoreCase))
        {
            EndMicTake();
            return;
        }

        RefreshRecDisk();
        EndMicTake();
        ShowNotice("録音を保存しました", name + " を書きました。解析するとファイルリストに入ります。");
        var recorded = _recIndex;
        if (advance && _recSettings.AutoAdvance)
            SelectNextMissing();
        if (_recIndex == recorded)
            FollowRecSelection();
        UpdateCommands();
    }

    async Task PlayGuideAsync(bool loop)
    {
        var shape = _guideShapeToken;
        var line = CurrentLine();
        if (line == null || _recPlayer == null || _recTaking && !loop)
            return;
        if (!loop)
            _recPlayer.Stop();
        var guide = _recGuideManual ? null : GuideWav();
        byte[] wav;
        if (!_recGuideManual)
        {
            _guideListening = true;
            if (guide == null)
            {
                _guideListening = false;
                ShowNotice("ガイドのファイルを選んでください", "WAV を選ぶと、同じ名前の txt が対になります。");
                return;
            }

            try
            {
                var info = new FileInfo(guide);
                if (info.Length > 32 * 1024 * 1024)
                {
                    ShowNotice("ガイドが大きすぎます", Path.GetFileName(guide));
                    return;
                }

                var speed = _recSettings.GuideSpeed;
                var pitch = _recSettings.GuidePitch;
                if (Math.Abs(speed - 1) < 0.001 && pitch == 0)
                    wav = await File.ReadAllBytesAsync(guide);
                else
                {
                    var audio = await Task.Run(() => WavAudio.Read(guide));
                    var shaped = await Task.Run(() => RecTone.ShapeGuide(
                        audio.Samples,
                        audio.Channels,
                        audio.SampleRate,
                        speed,
                        pitch));
                    wav = PreviewWav.Encode(shaped, audio.Channels, audio.SampleRate);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _guideListening = false;
                ShowNotice("ガイドを読めませんでした", ex.Message);
                return;
            }
        }
        else
        {
            _guideListening = false;
            if (!RecTone.TryHertz(CurrentNote(), out var hz))
                hz = 261.6255653005986;
            // 録音中のループにはカウントを入れない。聴くときだけ前に 4 拍付ける。
            wav = PreviewWav.Encode(
                RecTone.ManualGuide(
                    hz,
                    _recSettings.Bpm,
                    _recSettings.Moras,
                    RecTone.TargetRate,
                    _recSettings.CountIn,
                    _recSettings.CountIn && !loop,
                    _recSettings.CountBeats,
                    _recSettings.CountBeatsAfter,
                    _recSettings.MoraBeats),
                1,
                RecTone.TargetRate);
        }

        if (_recPlayer == null || (loop && !_recTaking) || shape != _guideShapeToken)
            return;
        _recPlayer.Volume = _recSettings.GuideVolume;
        await _recPlayer.PlayAsync(wav, loop);
    }

    string? GuideWav()
    {
        var path = _recGuide;
        if (!RecFolderStore.IsGuideWav(path) || path == null || !File.Exists(path))
            return null;
        return path;
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
                GuideManual = _recGuideManual,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice("録音の設定を保存できませんでした", ex.Message);
        }
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
                _ = PlayMicTakeAsync();
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
