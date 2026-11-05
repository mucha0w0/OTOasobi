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
    List<float> _take = [];
    bool _recording;
    bool _closed;
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

    public static async Task<IReadOnlyList<RecDeviceChoice>> DevicesAsync()
    {
        var list = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioCaptureSelector());
        var devices = new List<RecDeviceChoice>();
        foreach (var device in list)
            devices.Add(new RecDeviceChoice(device.Id, device.Name));
        return devices;
    }

    public async Task StartAsync(string? deviceId)
    {
        if (_closed)
            return;
        if (Running && DeviceId == deviceId)
            return;
        StopGraph();
        var settings = new AudioGraphSettings(AudioRenderCategory.Media);
        var created = await AudioGraph.CreateAsync(settings);
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
        Running = true;
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
        }
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
            var peak = 0f;
            foreach (var sample in samples)
                peak = Math.Max(peak, Math.Abs(sample));
            lock (_gate)
            {
                _channels = channels;
                _rate = Math.Max(1, (int)encoding.SampleRate);
                _peak = peak;
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
