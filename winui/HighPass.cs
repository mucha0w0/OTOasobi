namespace Otoasobi;

// 声より下の低い音を落とす。空調の音や、マイクスタンドの振動向け。
// カットオフは -3 dB の位置。12 dB/oct は 2 次、24 dB/oct は 4 次のバターワース。
sealed class HighPassEffect : AudioEffect
{
    public const string KindId = "highpass";
    public const double MinHz = 20;
    public const double MaxHz = 200;
    public const double DefaultHz = 80;
    public const int Slope12 = 12;
    public const int Slope24 = 24;
    public const int DefaultSlope = Slope12;

    const double Q12 = 0.7071067811865476;
    // 4 次は、カットオフで合わせて -3 dB になる Q の 2 次を 2 段つなぐ。
    static readonly double[] Q24 =
    [
        1 / (2 * Math.Sin(3 * Math.PI / 8)),
        1 / (2 * Math.Sin(Math.PI / 8)),
    ];

    double _hz = DefaultHz;
    int _slope = DefaultSlope;
    Biquad[][] _bank = [];
    int _rate;
    int _stages = 1;
    bool _coeffsDirty = true;

    public override string Kind => KindId;

    public override string Title => "ハイパス";

    public double Frequency
    {
        get => _hz;
        set
        {
            var next = ClampHz(value);
            if (next == _hz)
                return;
            _hz = next;
            _coeffsDirty = true;
        }
    }

    public int Slope
    {
        get => _slope;
        set
        {
            var next = ClampSlope(value);
            if (next == _slope)
                return;
            _slope = next;
            _coeffsDirty = true;
        }
    }

    public static double ClampHz(double hz)
    {
        if (!double.IsFinite(hz))
            return DefaultHz;
        return Math.Clamp(Math.Round(hz, 0, MidpointRounding.AwayFromZero), MinHz, MaxHz);
    }

    public static int ClampSlope(int slope) => slope == Slope24 ? Slope24 : Slope12;

    public override void Reset()
    {
        foreach (var row in _bank)
        {
            foreach (var stage in row)
            {
                stage.Z1 = 0;
                stage.Z2 = 0;
            }
        }
    }

    public override void Process(Span<float> interleaved, int channels, int sampleRate)
    {
        if (!Enabled || channels is < 1 or > 16 || sampleRate <= 0 || interleaved.Length < channels)
            return;

        Ensure(channels, sampleRate);
        var stages = _stages;
        var frames = interleaved.Length / channels;
        for (var n = 0; n < frames; n++)
        {
            for (var ch = 0; ch < channels; ch++)
            {
                var index = n * channels + ch;
                var x = interleaved[index];
                var filters = _bank[ch];
                for (var b = 0; b < stages; b++)
                    x = filters[b].Tick(x);
                interleaved[index] = x;
            }
        }
    }

    void Ensure(int channels, int sampleRate)
    {
        if (_bank.Length != channels || _rate != sampleRate)
        {
            _rate = sampleRate;
            _bank = new Biquad[channels][];
            for (var ch = 0; ch < channels; ch++)
            {
                _bank[ch] = [new Biquad(), new Biquad()];
            }

            _coeffsDirty = true;
        }

        if (!_coeffsDirty)
            return;

        _stages = _slope == Slope24 ? 2 : 1;
        for (var ch = 0; ch < channels; ch++)
        {
            for (var b = 0; b < 2; b++)
            {
                if (b < _stages)
                {
                    var q = _stages == 1 ? Q12 : Q24[b];
                    Configure(_bank[ch][b], _hz, q, sampleRate);
                }
                else
                {
                    Bypass(_bank[ch][b]);
                }
            }
        }

        _coeffsDirty = false;
    }

    static void Configure(Biquad stage, double frequency, double q, int sampleRate)
    {
        if (frequency <= 0 || q <= 0 || sampleRate <= frequency * 2)
        {
            Bypass(stage);
            return;
        }

        var w0 = 2 * Math.PI * frequency / sampleRate;
        var cos = Math.Cos(w0);
        var alpha = Math.Sin(w0) / (2 * q);
        var a0 = 1 + alpha;
        var pass = (1 + cos) / 2;
        stage.B0 = pass / a0;
        stage.B1 = -(1 + cos) / a0;
        stage.B2 = pass / a0;
        stage.A1 = (-2 * cos) / a0;
        stage.A2 = (1 - alpha) / a0;
        stage.Active = true;
    }

    static void Bypass(Biquad stage)
    {
        stage.B0 = 1;
        stage.B1 = 0;
        stage.B2 = 0;
        stage.A1 = 0;
        stage.A2 = 0;
        stage.Z1 = 0;
        stage.Z2 = 0;
        stage.Active = false;
    }

    sealed class Biquad
    {
        public double B0 = 1;
        public double B1;
        public double B2;
        public double A1;
        public double A2;
        public double Z1;
        public double Z2;
        public bool Active;

        public float Tick(float x)
        {
            if (!Active)
                return x;
            var y = B0 * x + Z1;
            Z1 = B1 * x - A1 * y + Z2;
            Z2 = B2 * x - A2 * y;
            if (!double.IsFinite(y) || !double.IsFinite(Z1) || !double.IsFinite(Z2))
            {
                Z1 = 0;
                Z2 = 0;
                return x;
            }

            if (Z1 is > -1e-18 and < 1e-18)
                Z1 = 0;
            if (Z2 is > -1e-18 and < 1e-18)
                Z2 = 0;
            return (float)y;
        }
    }
}
