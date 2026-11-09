using Microsoft.UI.Dispatching;
using Windows.Devices.Enumeration;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace Otoasobi;

// メモリ上の WAV を一度だけ再生する。ファイルには書き出さない。
sealed class PreviewPlayer : IDisposable
{
    readonly DispatcherQueue _queue;
    readonly MediaPlayer _player = new();
    MediaSource? _source;
    InMemoryRandomAccessStream? _stream;
    int _token;
    string? _outputId;
    string? _appliedOutputId;
    bool _live;
    bool _disposed;

    public PreviewPlayer(DispatcherQueue queue)
    {
        _queue = queue;
        _player.AudioCategory = MediaPlayerAudioCategory.Media;
        _player.CommandManager.IsEnabled = false;
        _player.MediaEnded += OnEnded;
        _player.MediaFailed += OnFailed;
    }

    public event EventHandler? Ended;

    public event EventHandler<string>? Failed;

    public double Volume
    {
        get => _player.Volume;
        set => _player.Volume = Math.Clamp(value, 0, 1);
    }

    public bool IsPlaying => _live;

    public double PositionSeconds =>
        _live ? _player.PlaybackSession.Position.TotalSeconds : 0;

    public void SetOutput(string? deviceId) =>
        _outputId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;

    public async Task ApplyOutputAsync()
    {
        if (_disposed)
            return;
        var id = _outputId;
        if (_appliedOutputId == id && (id == null || _player.AudioDevice != null))
            return;
        if (id == null)
        {
            _player.AudioDevice = null;
            _appliedOutputId = null;
            return;
        }

        var device = await DeviceInformation.CreateFromIdAsync(id);
        if (_disposed || _outputId != id)
            return;
        _player.AudioDevice = device;
        _appliedOutputId = id;
    }

    public Task<bool> PlayAsync(byte[] wav) => PlayAsync(wav, loop: false);

    public async Task<bool> PlayAsync(byte[] wav, bool loop)
    {
        if (_disposed)
            return false;
        try
        {
            await ApplyOutputAsync();
        }
        catch (Exception)
        {
            Failed?.Invoke(this, "出力デバイスを開けませんでした。");
            return false;
        }

        var token = ++_token;
        _live = false;
        ReleaseSource();
        var stream = new InMemoryRandomAccessStream();
        var writer = new DataWriter(stream);
        writer.WriteBytes(wav);
        await writer.StoreAsync();
        writer.DetachStream();
        writer.Dispose();
        stream.Seek(0);
        if (token != _token || _disposed)
        {
            stream.Dispose();
            return false;
        }

        var source = MediaSource.CreateFromStream(stream, "audio/wav");
        _stream = stream;
        _source = source;
        _live = true;
        _player.IsLoopingEnabled = loop;
        _player.Source = source;
        _player.Play();
        return token == _token && !_disposed;
    }

    public void Stop()
    {
        if (_disposed)
            return;
        _token++;
        _live = false;
        _player.IsLoopingEnabled = false;
        _player.Pause();
        _player.Source = null;
        ReleaseSource();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _token++;
        _live = false;
        _player.MediaEnded -= OnEnded;
        _player.MediaFailed -= OnFailed;
        _player.Source = null;
        ReleaseSource();
        _player.Dispose();
    }

    void OnEnded(MediaPlayer sender, object args)
    {
        if (!_live)
            return;
        var token = _token;
        _live = false;
        _queue.TryEnqueue(() =>
        {
            if (token != _token || _disposed)
                return;
            Ended?.Invoke(this, EventArgs.Empty);
        });
    }

    void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        if (!_live)
            return;
        var token = _token;
        _live = false;
        var message = string.IsNullOrWhiteSpace(args.ErrorMessage) ? "再生できませんでした。" : args.ErrorMessage;
        _queue.TryEnqueue(() =>
        {
            if (token != _token || _disposed)
                return;
            Failed?.Invoke(this, message);
        });
    }

    void ReleaseSource()
    {
        _source?.Dispose();
        _source = null;
        _stream?.Dispose();
        _stream = null;
    }
}

static class PreviewWav
{
    public static byte[] Encode(float[] samples, int channels, int sampleRate)
    {
        if (channels < 1 || sampleRate <= 0)
            throw new InvalidDataException("再生できない形式です。");
        var dataBytes = samples.Length * 2;
        using var memory = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(memory);
        writer.Write(0x46464952);
        writer.Write(36 + dataBytes);
        writer.Write(0x45564157);
        writer.Write(0x20746D66);
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write((ushort)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((ushort)(channels * 2));
        writer.Write((ushort)16);
        writer.Write(0x61746164);
        writer.Write(dataBytes);
        foreach (var sample in samples)
        {
            var scaled = Math.Clamp(sample, -1f, 1f) * 32767f;
            writer.Write((short)Math.Round(scaled));
        }

        return memory.ToArray();
    }
}
