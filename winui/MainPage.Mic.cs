using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Otoasobi;

public sealed partial class MainPage
{
    DispatcherQueueTimer? _micTimer;
    RecTrace? _micTrace;
    PreviewPlayer? _micPlayer;
    int _micTakeToken;
    int _micListenToken;
    string? _micShownPath;
    string? _micListenError;
    bool _micPlaying;
    double _micHold;
    long _micHoldAt;

    void WireMic()
    {
        _micTrace = new RecTrace();
        MicWaveHost.Children.Add(_micTrace);
        var queue = DispatcherQueue;
        if (queue == null)
            return;
        _micPlayer = new PreviewPlayer(queue);
        _micPlayer.SetOutput(_recSettings.OutputDeviceId);
        _micPlayer.Ended += (_, _) => StopMicPlayback();
        _micPlayer.Failed += (_, message) =>
        {
            StopMicPlayback();
            ShowNotice("再生できませんでした", message);
        };
    }

    void MicSync()
    {
        if (MicWaveHost == null || _recCapture == null)
            return;
        var watch = EditorView.Visibility == Visibility.Visible && Dock.Contains("mic");
        if (!watch)
            _micTimer?.Stop();
        else
        {
            _micTimer ??= CreateMicTimer();
            if (!_micTimer.IsRunning)
                _micTimer.Start();
            RefreshMicTake();
        }

        _ = ApplyMicListenAsync();
    }

    DispatcherQueueTimer CreateMicTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(33);
        timer.Tick += (_, _) => MicTick();
        return timer;
    }

    void MicTick()
    {
        if (_micTrace == null || _recCapture == null)
            return;
        var live = _micShownPath == null;
        if (_recCapture.Running)
        {
            if (live)
            {
                var (samples, rate) = _recCapture.CopyLive();
                _micTrace.SetLive(samples, rate);
            }

            ShowMicLevel(_recCapture.Peak);
        }
        else
        {
            if (live)
                _micTrace.Clear();
            ShowMicLevel(0, stopped: true);
        }

        if (_micPlaying && _micPlayer != null && _micShownPath != null)
            _micTrace.SetPlayhead(_micPlayer.PositionSeconds);
    }

    void ShowMicLevel(float peak, bool stopped = false)
    {
        if (MicLevelTrack == null || MicLevelFill == null || MicLevelHold == null || MicLevelText == null)
            return;
        if (stopped)
        {
            _micHold = 0;
            MicLevelFill.Height = 0;
            MicLevelHold.Visibility = Visibility.Collapsed;
            MicLevelText.Text = "停止中";
            return;
        }

        var level = RecMeter.Normalize(peak);
        var now = Environment.TickCount64;
        if (level >= _micHold)
        {
            _micHold = level;
            _micHoldAt = now;
        }
        else if (now - _micHoldAt > 1000)
        {
            _micHold = level;
        }

        var inner = Math.Max(0, MicLevelTrack.ActualHeight - 6);
        MicLevelFill.Height = inner * level;
        var db = peak <= 0.00001f ? double.NegativeInfinity : 20 * Math.Log10(Math.Clamp(peak, 1e-8f, 1f));
        var key = db >= -3 ? "SystemFillColorCriticalBrush" : "AccentFillColorDefaultBrush";
        MicLevelFill.Background = AppearanceTheme.Brush(AppearanceTheme.Resolve(this), key) ?? MicLevelFill.Background;
        MicLevelHold.Margin = new Thickness(3, 0, 3, Math.Max(3, 3 + inner * _micHold - 2));
        MicLevelHold.Visibility = _micHold <= 0 ? Visibility.Collapsed : Visibility.Visible;
        MicLevelText.Text = double.IsNegativeInfinity(db) ? "-∞" : $"{db:0}";
    }

    void BeginMicTake()
    {
        StopMicPlayback();
        if (_micShownPath != null)
            return;
        _micTakeToken++;
        _micTrace?.Clear();
        if (MicWaveText != null)
            MicWaveText.Text = "マイク入力";
    }

    void EndMicTake() => RefreshMicTake(reload: true);

    void RefreshMicTake(bool reload = false)
    {
        if (MicWaveText == null || _recTaking)
            return;
        var line = CurrentLine();
        string? path = null;
        if (line != null && _folder != null)
        {
            var name = RecNames.WavName(line.Sing);
            if (name.Length > 0 && _recWavNames.Contains(name))
                path = Path.Combine(_folder, name);
        }

        if (!reload && string.Equals(path, _micShownPath, StringComparison.OrdinalIgnoreCase))
            return;
        StopMicPlayback();
        _micShownPath = path;
        var token = ++_micTakeToken;
        if (path == null)
        {
            _micTrace?.Clear();
            MicWaveText.Text = "マイク入力";
            return;
        }

        MicWaveText.Text = Path.GetFileName(path);
        _ = LoadMicTakeAsync(path, token);
    }

    async Task LoadMicTakeAsync(string path, int token)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 64 * 1024 * 1024)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (token != _micTakeToken)
                        return;
                    _micTrace?.Clear();
                    MicWaveText.Text = "WAV が大きすぎます";
                });
                return;
            }

            var drawn = await Task.Run(() =>
            {
                var audio = WavAudio.Read(path);
                var mono = RecTone.Downmix(audio.Samples, Math.Max(1, audio.Channels));
                var track = new RecPeakTrack();
                track.Append(mono);
                var (min, max) = track.Snapshot();
                return (min, max, mono.Length, Math.Max(1, audio.SampleRate));
            });
            DispatcherQueue.TryEnqueue(() =>
            {
                if (token != _micTakeToken || _recTaking || _micShownPath == null)
                    return;
                _micTrace?.SetBuckets(drawn.min, drawn.max, drawn.Length, drawn.Item4);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (token != _micTakeToken)
                    return;
                _micTrace?.Clear();
                MicWaveText.Text = "WAV を読めませんでした";
            });
        }
    }

    async Task ApplyMicListenAsync()
    {
        if (_recCapture == null || MicListenSwitch == null)
            return;
        var token = ++_micListenToken;
        var on = MicListenSwitch.IsOn
            && EditorView.Visibility == Visibility.Visible
            && Dock.Contains("mic")
            && _recCapture.Running;
        try
        {
            await _recCapture.ApplyListenAsync(on, _recSettings.OutputDeviceId);
        }
        catch (Exception)
        {
            if (token != _micListenToken)
                return;
            _micListenError = "出力デバイスを開けませんでした。";
            ShowRecDeviceError();
            return;
        }

        if (token != _micListenToken)
            return;
        _micListenError = _recCapture.ListenError;
        ShowRecDeviceError();
    }

    void MicListen_Toggled(object sender, RoutedEventArgs e)
    {
        if (_recApplying)
            return;
        _ = ApplyMicListenAsync();
    }

    void MicPlay_Click(object sender, RoutedEventArgs e) => _ = PlayMicTakeAsync();

    async Task PlayMicTakeAsync()
    {
        if (_micPlaying)
        {
            StopMicPlayback();
            return;
        }

        if (_micPlayer == null || _recTaking || _folder == null)
            return;
        var line = CurrentLine();
        if (line == null)
            return;
        var path = Path.Combine(_folder, RecNames.WavName(line.Sing));
        if (!File.Exists(path))
            return;
        try
        {
            var wav = await File.ReadAllBytesAsync(path);
            if (_micPlayer == null || _recTaking || _micShownPath == null)
                return;
            _micPlayer.Volume = 1;
            _micPlaying = true;
            UpdateRecButtons();
            var started = await _micPlayer.PlayAsync(wav);
            if (!started)
                StopMicPlayback();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StopMicPlayback();
            ShowNotice("テイクを再生できませんでした", ex.Message);
        }
    }

    void StopMicPlayback()
    {
        var was = _micPlaying;
        _micPlaying = false;
        _micPlayer?.Stop();
        _micTrace?.SetPlayhead(null);
        if (was || MicPlayButton?.IsEnabled == true)
            UpdateRecButtons();
    }
}
