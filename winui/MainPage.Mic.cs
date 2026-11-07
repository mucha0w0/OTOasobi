using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Otoasobi;

public sealed partial class MainPage
{
    DispatcherQueueTimer? _micTimer;
    RecTrace? _micLive;
    RecTrace? _micTake;
    PreviewPlayer? _micPlayer;
    readonly RecPeakTrack _micPeaks = new();
    readonly List<float> _micNew = [];
    int _micTakeFrame;
    int _micTakeToken;
    int _micListenToken;
    string? _micShownPath;
    string? _micListenError;
    bool _micPlaying;

    void WireMic()
    {
        _micLive = new RecTrace();
        _micTake = new RecTrace();
        MicLiveHost.Children.Add(_micLive);
        MicTakeHost.Children.Add(_micTake);
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
        if (MicLiveHost == null || _recCapture == null)
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
        if (_micLive == null || _recCapture == null)
            return;
        if (_recCapture.Running)
        {
            var (samples, rate) = _recCapture.CopyLive();
            _micLive.SetLive(samples, rate);
            ShowMicLevel(_recCapture.Peak);
        }
        else
        {
            _micLive.Clear();
            ShowMicLevel(0, stopped: true);
        }

        if (_recTaking)
        {
            _micNew.Clear();
            _recCapture.CopyTakeMono(_micTakeFrame, _micNew, out var rate, out var frames);
            _micTakeFrame = frames;
            if (_micNew.Count > 0)
            {
                _micPeaks.Append(CollectionsMarshal.AsSpan(_micNew));
                var (min, max) = _micPeaks.Snapshot();
                _micTake?.SetBuckets(min, max, _micPeaks.Samples, Math.Max(1, rate));
                MicTakeText.Text = "録音中";
            }

            return;
        }

        if (_micPlaying && _micPlayer != null)
            _micTake?.SetPlayhead(_micPlayer.PositionSeconds);
    }

    void ShowMicLevel(float peak, bool stopped = false)
    {
        if (MicLevelTrack == null || MicLevelFill == null || MicLevelText == null)
            return;
        if (stopped)
        {
            MicLevelFill.Width = 0;
            MicLevelText.Text = "停止中";
            return;
        }

        var clamped = Math.Clamp(peak, 0f, 1f);
        MicLevelFill.Width = MicLevelTrack.ActualWidth * clamped;
        var key = clamped >= 0.98f ? "SystemFillColorCriticalBrush" : "AccentFillColorDefaultBrush";
        MicLevelFill.Background = AppearanceTheme.Brush(AppearanceTheme.Resolve(this), key) ?? MicLevelFill.Background;
        MicLevelText.Text = clamped <= 0.00001f ? "-∞ dB" : $"{20 * Math.Log10(clamped):0.0} dB";
    }

    void BeginMicTake()
    {
        StopMicPlayback();
        _micPeaks.Clear();
        _micTakeFrame = 0;
        _micShownPath = null;
        _micTakeToken++;
        _micTake?.Clear();
        if (MicTakeText != null)
            MicTakeText.Text = "録音中";
    }

    void EndMicTake() => RefreshMicTake(reload: true);

    void RefreshMicTake(bool reload = false)
    {
        if (MicTakeText == null || _recTaking)
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
            _micTake?.Clear();
            MicTakeText.Text = line == null ? "行を選ぶと、録音した WAV が出ます。" : "まだ録音していません";
            return;
        }

        MicTakeText.Text = Path.GetFileName(path);
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
                    _micTake?.Clear();
                    MicTakeText.Text = "WAV が大きすぎます";
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
                if (token != _micTakeToken || _recTaking)
                    return;
                _micTake?.SetBuckets(drawn.min, drawn.max, drawn.Length, drawn.Item4);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (token != _micTakeToken)
                    return;
                _micTake?.Clear();
                MicTakeText.Text = "WAV を読めませんでした";
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
            if (_micPlayer == null || _recTaking)
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
        _micTake?.SetPlayhead(null);
        if (was || MicPlayButton?.IsEnabled == true)
            UpdateRecButtons();
    }
}
