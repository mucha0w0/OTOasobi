using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Capture;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;
using Windows.Media.Render;

namespace Otoasobi;

sealed record RecDeviceChoice(string Id, string Label);

// マイクの監視と、テイクの間だけサンプルを貯める。画面への通知は呼び出し側のタイマー。
sealed class RecCapture : IDisposable
{
    readonly object _gate = new();
    AudioGraph? _graph;
    AudioDeviceInputNode? _input;
    AudioFrameOutputNode? _frames;
    AudioDeviceOutputNode? _output;
    readonly RecLiveBuffer _live = new();
    List<float> _take = [];
    bool _recording;
    bool _closed;
    bool _listenWanted;
    int _listenToken;
    string? _listenDevice;
    string? _outputDevice;
    int _channels = 1;
    int _rate = RecTone.TargetRate;
    float _peak;

    public bool Running { get; private set; }

    public string? DeviceId { get; private set; }

    public float Peak
    {
        get
        {
            lock (_gate)
                return _peak;
        }
    }

    public string? ListenError { get; private set; }

    public static Task<IReadOnlyList<RecDeviceChoice>> DevicesAsync() =>
        ListDevicesAsync(MediaDevice.GetAudioCaptureSelector());

    public static Task<IReadOnlyList<RecDeviceChoice>> PlaybackDevicesAsync() =>
        ListDevicesAsync(MediaDevice.GetAudioRenderSelector());

    static async Task<IReadOnlyList<RecDeviceChoice>> ListDevicesAsync(string selector)
    {
        var list = await DeviceInformation.FindAllAsync(selector);
        var devices = new List<RecDeviceChoice>();
        foreach (var device in list)
            devices.Add(new RecDeviceChoice(device.Id, device.Name));
        return devices;
    }

    public string? RenderDeviceId { get; private set; }

    public async Task StartAsync(string? deviceId, string? renderDeviceId)
    {
        if (_closed)
            return;
        renderDeviceId = string.IsNullOrWhiteSpace(renderDeviceId) ? null : renderDeviceId;
        if (Running && DeviceId == deviceId && RenderDeviceId == renderDeviceId)
            return;
        StopGraph();
        var settings = new AudioGraphSettings(AudioRenderCategory.Media);
        if (renderDeviceId != null)
        {
            try
            {
                settings.PrimaryRenderDevice = await DeviceInformation.CreateFromIdAsync(renderDeviceId);
            }
            catch (Exception)
            {
                settings = new AudioGraphSettings(AudioRenderCategory.Media);
                renderDeviceId = null;
            }
        }

        var created = await AudioGraph.CreateAsync(settings);
        if ((created.Status != AudioGraphCreationStatus.Success || created.Graph == null) && renderDeviceId != null)
        {
            created.Graph?.Dispose();
            settings = new AudioGraphSettings(AudioRenderCategory.Media);
            renderDeviceId = null;
            created = await AudioGraph.CreateAsync(settings);
        }
        if (created.Status != AudioGraphCreationStatus.Success || created.Graph == null)
            throw new InvalidOperationException(GraphMessage(created.Status));
        if (_closed)
        {
            created.Graph.Dispose();
            return;
        }

        var graph = created.Graph;
        CreateAudioDeviceInputNodeResult input;
        if (string.IsNullOrEmpty(deviceId))
        {
            input = await graph.CreateDeviceInputNodeAsync(MediaCategory.Media);
        }
        else
        {
            var device = await DeviceInformation.CreateFromIdAsync(deviceId);
            input = await graph.CreateDeviceInputNodeAsync(MediaCategory.Media, graph.EncodingProperties, device);
        }

        if (input.Status != AudioDeviceNodeCreationStatus.Success || input.DeviceInputNode == null)
        {
            graph.Dispose();
            throw new InvalidOperationException(DeviceMessage(input.Status));
        }

        var frames = graph.CreateFrameOutputNode();
        input.DeviceInputNode.AddOutgoingConnection(frames);
        _graph = graph;
        _input = input.DeviceInputNode;
        _frames = frames;
        DeviceId = deviceId;
        var encoding = frames.EncodingProperties;
        lock (_gate)
        {
            _channels = Math.Max(1, (int)encoding.ChannelCount);
            _rate = Math.Max(1, (int)encoding.SampleRate);
            _peak = 0;
        }

        graph.QuantumStarted += OnQuantum;
        graph.Start();
        RenderDeviceId = renderDeviceId;
        Running = true;
    }

    public (float[] Samples, int Rate) CopyLive()
    {
        lock (_gate)
            return _live.Snapshot();
    }

    public async Task ApplyListenAsync(bool on, string? deviceId)
    {
        _listenWanted = on;
        _listenDevice = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
        var token = ++_listenToken;
        ListenError = null;
        if (!on || !Running || _graph == null || _input == null)
        {
            RemoveListen();
            return;
        }

        if (_output != null && _outputDevice == _listenDevice)
            return;
        RemoveListen();
        var graph = _graph;
        var input = _input;
        if (_listenDevice != RenderDeviceId)
        {
            ListenError = "出力デバイスを開けませんでした。";
            return;
        }

        var created = await graph.CreateDeviceOutputNodeAsync();

        if (token != _listenToken || !_listenWanted || !ReferenceEquals(_graph, graph) || _input == null)
        {
            created.DeviceOutputNode?.Dispose();
            return;
        }

        if (created.Status != AudioDeviceNodeCreationStatus.Success || created.DeviceOutputNode == null)
        {
            created.DeviceOutputNode?.Dispose();
            ListenError = "出力デバイスを開けませんでした。";
            return;
        }

        input.AddOutgoingConnection(created.DeviceOutputNode);
        _output = created.DeviceOutputNode;
        _outputDevice = _listenDevice;
    }

    public void Stop()
    {
        if (_closed)
            return;
        StopGraph();
    }

    public void BeginTake()
    {
        lock (_gate)
        {
            _take = [];
            _recording = true;
        }
    }

    public (float[] Samples, int Channels, int Rate) EndTake()
    {
        lock (_gate)
        {
            _recording = false;
            var samples = _take.ToArray();
            _take = [];
            return (samples, _channels, _rate);
        }
    }

    public void Dispose()
    {
        _closed = true;
        StopGraph();
    }

    void StopGraph()
    {
        Running = false;
        RenderDeviceId = null;
        _listenToken++;
        RemoveListen();
        if (_graph != null)
        {
            _graph.QuantumStarted -= OnQuantum;
            _graph.Stop();
            _graph.Dispose();
        }

        _input?.Dispose();
        _frames?.Dispose();
        _graph = null;
        _input = null;
        _frames = null;
        lock (_gate)
        {
            _recording = false;
            _peak = 0;
            _live.Clear();
        }
    }

    void RemoveListen()
    {
        if (_output == null)
            return;
        if (_input != null)
            _input.RemoveOutgoingConnection(_output);
        _output.Dispose();
        _output = null;
        _outputDevice = null;
    }

    unsafe void OnQuantum(AudioGraph sender, object args)
    {
        if (_closed || _frames == null)
            return;
        var frame = _frames.GetFrame();
        using (var buffer = frame.LockBuffer(AudioBufferAccessMode.Read))
        {
            var length = (int)buffer.Length;
            if (length <= 0)
                return;
            using var reference = buffer.CreateReference();
            ((IMemoryBufferByteAccess)reference).GetBuffer(out var data, out _);
            var encoding = _frames.EncodingProperties;
            var bits = (int)encoding.BitsPerSample;
            var channels = Math.Max(1, (int)encoding.ChannelCount);
            var sampleBytes = Math.Max(1, bits / 8);
            var count = length / sampleBytes;
            if (count <= 0)
                return;
            var samples = new float[count];
            var isFloat = encoding.Subtype == MediaEncodingSubtypes.Float;
            for (var i = 0; i < count; i++)
                samples[i] = Decode(data, i * sampleBytes, bits, isFloat);
            var frames = count / channels;
            var mono = frames > 0 ? new float[frames] : [];
            var peak = 0f;
            for (var index = 0; index < frames; index++)
            {
                double sum = 0;
                for (var channel = 0; channel < channels; channel++)
                {
                    var sample = samples[index * channels + channel];
                    sum += sample;
                    peak = Math.Max(peak, Math.Abs(sample));
                }

                mono[index] = (float)(sum / channels);
            }

            for (var i = frames * channels; i < count; i++)
                peak = Math.Max(peak, Math.Abs(samples[i]));
            var rate = Math.Max(1, (int)encoding.SampleRate);
            lock (_gate)
            {
                _channels = channels;
                _rate = rate;
                _peak = peak;
                _live.Write(mono, rate);
                if (_recording)
                    _take.AddRange(samples);
            }
        }
    }

    static unsafe float Decode(byte* data, int offset, int bits, bool isFloat)
    {
        if (isFloat && bits == 32)
            return *(float*)(data + offset);
        if (bits == 16)
            return *(short*)(data + offset) / 32768f;
        if (bits == 32)
            return *(int*)(data + offset) / 2147483648f;
        return 0;
    }

    static string GraphMessage(AudioGraphCreationStatus status) => status switch
    {
        AudioGraphCreationStatus.DeviceNotAvailable => "マイクを開けませんでした。",
        _ => "音声デバイスを準備できませんでした。",
    };

    static string DeviceMessage(AudioDeviceNodeCreationStatus status) => status switch
    {
        AudioDeviceNodeCreationStatus.DeviceNotAvailable => "マイクを開けませんでした。",
        AudioDeviceNodeCreationStatus.AccessDenied => "マイクの使用が許可されていません。Windows のプライバシー設定でマイクを許可してください。",
        AudioDeviceNodeCreationStatus.FormatNotSupported => "このマイクの形式には対応していません。",
        _ => "マイクを開けませんでした。",
    };

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
}
