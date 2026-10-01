using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Otoasobi;

public sealed partial class EffectRack
{
    enum ListenSide
    {
        None,
        Dry,
        Wet,
    }

    PreviewPlayer? _preview;
    string? _listenFile;
    int _listenGeneration;
    ListenSide _listenSide;
    bool _listenBusy;
    bool _syncingListenMenu;

    public void StopListening()
    {
        _listenGeneration++;
        QuietListen();
    }

    void BindPreviewPlayer()
    {
        _preview = new PreviewPlayer(DispatcherQueue);
        _preview.Ended += (_, _) =>
        {
            _listenSide = ListenSide.None;
            _listenBusy = false;
            UpdateListenButtons();
        };
        _preview.Failed += (_, message) =>
        {
            var processed = _listenSide == ListenSide.Wet;
            _listenSide = ListenSide.None;
            _listenBusy = false;
            ShowListenStatus(processed, message, error: true);
            UpdateListenButtons();
        };
    }

    void Rack_Unloaded(object sender, RoutedEventArgs e)
    {
        _listenGeneration++;
        _preview?.Dispose();
        _preview = null;
    }

    void FillListenMenus()
    {
        _syncingListenMenu = true;
        try
        {
            DryFileMenu.Items.Clear();
            WetFileMenu.Items.Clear();
            if (_folder != null)
            {
                foreach (var file in NoiseSources.ListOriginal(_folder))
                {
                    DryFileMenu.Items.Add(file.Name);
                    WetFileMenu.Items.Add(file.Name);
                }
            }

            var hasFiles = DryFileMenu.Items.Count > 0;
            var placeholder = hasFiles ? "原音を選ぶ" : "WAV がありません";
            DryFileMenu.IsEnabled = hasFiles;
            WetFileMenu.IsEnabled = hasFiles;
            DryFileMenu.PlaceholderText = placeholder;
            WetFileMenu.PlaceholderText = placeholder;
            const string tip = "original フォルダの WAV を選びます";
            ToolTipService.SetToolTip(DryFileMenu, tip);
            ToolTipService.SetToolTip(WetFileMenu, tip);
            _listenFile = null;
            ClearListenStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DryFileMenu.IsEnabled = false;
            WetFileMenu.IsEnabled = false;
            DryFileMenu.PlaceholderText = "WAV がありません";
            WetFileMenu.PlaceholderText = "WAV がありません";
            ShowListenStatus(processed: false, ex.Message, error: true);
        }
        finally
        {
            _syncingListenMenu = false;
            UpdateListenButtons();
        }
    }

    void ListenFile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingListenMenu || sender is not ComboBox box)
            return;
        var name = box.SelectedItem as string;
        _syncingListenMenu = true;
        try
        {
            if (!ReferenceEquals(box, DryFileMenu))
                SelectListenFile(DryFileMenu, name);
            if (!ReferenceEquals(box, WetFileMenu))
                SelectListenFile(WetFileMenu, name);
            var tip = string.IsNullOrEmpty(name) ? "original フォルダの WAV を選びます" : name;
            ToolTipService.SetToolTip(DryFileMenu, tip);
            ToolTipService.SetToolTip(WetFileMenu, tip);
        }
        finally
        {
            _syncingListenMenu = false;
        }

        if (string.Equals(_listenFile, name, StringComparison.OrdinalIgnoreCase))
            return;
        _listenFile = name;
        StopListening();
        ClearListenStatus();
        UpdateListenButtons();
    }

    static void SelectListenFile(ComboBox box, string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            box.SelectedItem = null;
            return;
        }

        foreach (var item in box.Items)
        {
            if (item is string text && string.Equals(text, name, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = text;
                return;
            }
        }

        box.SelectedItem = null;
    }

    void DryPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_listenSide == ListenSide.Dry)
        {
            StopListening();
            return;
        }

        _ = PlayListenAsync(processed: false);
    }

    void WetPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_listenSide == ListenSide.Wet)
        {
            StopListening();
            return;
        }

        _ = PlayListenAsync(processed: true);
    }

    async Task PlayListenAsync(bool processed)
    {
        var folder = _folder;
        var name = _listenFile;
        var preview = _preview;
        if (folder == null || string.IsNullOrEmpty(name) || preview == null)
            return;
        var path = NoiseSources.Resolve(folder, name);
        if (path == null)
        {
            ShowListenStatus(processed, "WAV が見つかりません。", error: true);
            return;
        }

        var generation = ++_listenGeneration;
        preview.Stop();
        _listenSide = processed ? ListenSide.Wet : ListenSide.Dry;
        _listenBusy = true;
        ShowListenStatus(processed, "読み込んでいます。", error: false);
        if (processed)
            ClearListenStatus(dryOnly: true);
        else
            ClearListenStatus(wetOnly: true);
        UpdateListenButtons();

        byte[] wav;
        double seconds;
        try
        {
            (wav, seconds) = await Task.Run(() => RenderListen(path, processed));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (generation != _listenGeneration)
                return;
            _listenSide = ListenSide.None;
            _listenBusy = false;
            ShowListenStatus(processed, ex.Message, error: true);
            UpdateListenButtons();
            return;
        }

        if (generation != _listenGeneration || preview != _preview)
            return;
        if (wav.Length <= 44)
        {
            _listenSide = ListenSide.None;
            _listenBusy = false;
            ShowListenStatus(processed, "波形がありません。", error: true);
            UpdateListenButtons();
            return;
        }

        bool started;
        try
        {
            started = await preview.PlayAsync(wav);
        }
        catch (Exception ex)
        {
            if (generation != _listenGeneration)
                return;
            _listenSide = ListenSide.None;
            _listenBusy = false;
            ShowListenStatus(processed, string.IsNullOrWhiteSpace(ex.Message) ? "再生できませんでした。" : ex.Message, error: true);
            UpdateListenButtons();
            return;
        }

        if (!started || generation != _listenGeneration)
            return;

        if (generation != _listenGeneration)
            return;
        _listenBusy = false;
        ShowListenStatus(processed, seconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "秒", error: false);
        UpdateListenButtons();
    }

    (byte[] Wav, double Seconds) RenderListen(string path, bool processed)
    {
        var audio = WavAudio.Read(path);
        var samples = audio.Samples;
        if (samples.Length == 0 || audio.Channels < 1 || audio.SampleRate <= 0)
            throw new InvalidDataException("波形がありません。");
        if (processed)
        {
            samples = samples.ToArray();
            _chain.Render(samples, audio.Channels, audio.SampleRate);
        }

        var seconds = samples.Length / (double)audio.Channels / audio.SampleRate;
        return (PreviewWav.Encode(samples, audio.Channels, audio.SampleRate), seconds);
    }

    void InvalidateProcessedPreview()
    {
        if (_listenSide != ListenSide.Wet)
            return;
        _listenGeneration++;
        _preview?.Stop();
        _listenSide = ListenSide.None;
        _listenBusy = false;
        ClearListenStatus(wetOnly: true);
        UpdateListenButtons();
    }

    void QuietListen()
    {
        _listenSide = ListenSide.None;
        _listenBusy = false;
        _preview?.Stop();
        UpdateListenButtons();
    }

    void UpdateListenButtons()
    {
        var hasFile = !string.IsNullOrEmpty(_listenFile);
        PaintPlayButton(
            DryPlayButton,
            DryPlayIcon,
            DryStopIcon,
            DryPlayRing,
            active: _listenSide == ListenSide.Dry,
            hasFile,
            "処理前を再生",
            "処理前の再生を止める");
        PaintPlayButton(
            WetPlayButton,
            WetPlayIcon,
            WetStopIcon,
            WetPlayRing,
            active: _listenSide == ListenSide.Wet,
            hasFile,
            "処理後を再生",
            "処理後の再生を止める");
    }

    void PaintPlayButton(
        Button button,
        Microsoft.UI.Xaml.Shapes.Path play,
        Microsoft.UI.Xaml.Shapes.Path stop,
        ProgressRing ring,
        bool active,
        bool hasFile,
        string playName,
        string stopName)
    {
        var busy = active && _listenBusy;
        button.IsEnabled = hasFile;
        play.Visibility = !busy && !active ? Visibility.Visible : Visibility.Collapsed;
        stop.Visibility = !busy && active ? Visibility.Visible : Visibility.Collapsed;
        ring.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ring.IsActive = busy;
        var name = active ? stopName : playName;
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, active ? "停止" : "再生");
    }

    void ShowListenStatus(bool processed, string text, bool error)
    {
        var block = processed ? WetStatus : DryStatus;
        if (!error)
        {
            block.Text = "";
            block.Visibility = Visibility.Collapsed;
            return;
        }

        block.Text = text;
        block.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        var key = error ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush";
        if (Application.Current.Resources.TryGetValue(key, out var brush) && brush is Brush paint)
            block.Foreground = paint;
    }

    void ClearListenStatus(bool dryOnly = false, bool wetOnly = false)
    {
        if (!wetOnly)
        {
            DryStatus.Text = "";
            DryStatus.Visibility = Visibility.Collapsed;
        }

        if (!dryOnly)
        {
            WetStatus.Text = "";
            WetStatus.Visibility = Visibility.Collapsed;
        }
    }
}
