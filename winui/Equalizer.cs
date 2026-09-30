using System.Globalization;
using System.Numerics;

namespace Otoasobi;

// 1 オクターブごとのピーキング。ゲイン 0 dB の帯域は通さない。
// Q は 2。中心では指定した dB だけ変わり、隣の帯域への漏れは小さい。
sealed class EqualizerEffect : AudioEffect
{
    public const string KindId = "equalizer";
    public const int BandCount = 8;
    public const double MinDb = -12;
    public const double MaxDb = 12;
    public const double Q = 2;
    public const double DisplayRate = 48000;

    public static readonly double[] Frequencies = [63, 125, 250, 500, 1000, 2000, 4000, 8000];

    readonly double[] _gains = new double[BandCount];
    Biquad[][] _bank = [];
    int _rate;
    bool _coeffsDirty = true;

    public override string Kind => KindId;

    public override string Title => "イコライザー";

    public IReadOnlyList<double> Gains => _gains;

    public static double QuantizeGain(double db, bool magnet)
    {
        if (!double.IsFinite(db))
            return 0;
        db = Math.Clamp(db, MinDb, MaxDb);
        if (magnet && Math.Abs(db) < 0.2)
            return 0;
        return Math.Round(db, 1, MidpointRounding.AwayFromZero);
    }

    public static string FormatFrequency(double hz)
    {
        if (hz >= 1000)
        {
            var k = hz / 1000;
            var text = Math.Abs(k - Math.Round(k)) < 0.05
                ? Math.Round(k).ToString("0", CultureInfo.InvariantCulture)
                : k.ToString("0.#", CultureInfo.InvariantCulture);
            return text + " kHz";
        }

        return hz.ToString("0", CultureInfo.InvariantCulture) + " Hz";
    }

    public void SetGains(IReadOnlyList<double>? gains)
    {
        for (var i = 0; i < BandCount; i++)
        {
            var value = gains != null && i < gains.Count
                ? QuantizeGain(gains[i], magnet: false)
                : 0;
            if (value == _gains[i])
                continue;
            _gains[i] = value;
            _coeffsDirty = true;
        }
    }

    public double[] CopyGains()
    {
        var copy = new double[BandCount];
        _gains.CopyTo(copy);
        return copy;
    }

    public bool Matches(IReadOnlyList<double> gains)
    {
        if (gains.Count < BandCount)
            return false;
        for (var i = 0; i < BandCount; i++)
        {
            if (Math.Round(_gains[i], 1) != Math.Round(gains[i], 1))
                return false;
        }

        return true;
    }

    public static double ResponseDb(ReadOnlySpan<double> gains, double frequency, double sampleRate)
    {
        if (frequency <= 0 || sampleRate <= 0)
            return 0;
        double sum = 0;
        var count = Math.Min(gains.Length, Frequencies.Length);
        for (var i = 0; i < count; i++)
            sum += BandDb(gains[i], Frequencies[i], frequency, sampleRate);
        return sum;
    }

    public override void Reset()
    {
        foreach (var row in _bank)
        {
            foreach (var band in row)
            {
                band.Z1 = 0;
                band.Z2 = 0;
            }
        }
    }

    public override void Process(Span<float> interleaved, int channels, int sampleRate)
    {
        if (!Enabled || channels is < 1 or > 16 || sampleRate <= 0 || interleaved.Length < channels)
            return;
        if (!HasGain())
        {
            Reset();
            return;
        }

        Ensure(channels, sampleRate);
        var frames = interleaved.Length / channels;
        for (var n = 0; n < frames; n++)
        {
            for (var ch = 0; ch < channels; ch++)
            {
                var index = n * channels + ch;
                var x = interleaved[index];
                var filters = _bank[ch];
                for (var b = 0; b < filters.Length; b++)
                    x = filters[b].Tick(x);
                interleaved[index] = x;
            }
        }
    }

    bool HasGain()
    {
        foreach (var gain in _gains)
        {
            if (Math.Abs(gain) >= 0.05)
                return true;
        }

        return false;
    }

    void Ensure(int channels, int sampleRate)
    {
        if (_bank.Length != channels || _rate != sampleRate)
        {
            _rate = sampleRate;
            _bank = new Biquad[channels][];
            for (var ch = 0; ch < channels; ch++)
            {
                _bank[ch] = new Biquad[BandCount];
                for (var b = 0; b < BandCount; b++)
                    _bank[ch][b] = new();
            }

            _coeffsDirty = true;
        }

        if (!_coeffsDirty)
            return;
        for (var ch = 0; ch < channels; ch++)
        {
            for (var b = 0; b < BandCount; b++)
                Configure(_bank[ch][b], _gains[b], Frequencies[b], sampleRate);
        }

        _coeffsDirty = false;
    }

    static void Configure(Biquad band, double gainDb, double frequency, int sampleRate)
    {
        if (Math.Abs(gainDb) < 0.05 || frequency <= 0 || sampleRate <= frequency * 2)
        {
            band.B0 = 1;
            band.B1 = 0;
            band.B2 = 0;
            band.A1 = 0;
            band.A2 = 0;
            band.Z1 = 0;
            band.Z2 = 0;
            band.Active = false;
            return;
        }

        Coefficients(gainDb, frequency, sampleRate, out var b0, out var b1, out var b2, out var a1, out var a2);
        band.B0 = b0;
        band.B1 = b1;
        band.B2 = b2;
        band.A1 = a1;
        band.A2 = a2;
        band.Active = true;
    }

    static double BandDb(double gainDb, double center, double frequency, double sampleRate)
    {
        if (Math.Abs(gainDb) < 0.049 || center <= 0 || frequency <= 0)
            return 0;
        if (sampleRate <= center * 2 || sampleRate <= frequency * 2)
            return 0;
        Coefficients(gainDb, center, sampleRate, out var b0, out var b1, out var b2, out var a1, out var a2);
        var z = Complex.FromPolarCoordinates(1, -2 * Math.PI * frequency / sampleRate);
        var z2 = z * z;
        var response = (b0 + b1 * z + b2 * z2) / (1 + a1 * z + a2 * z2);
        var magnitude = response.Magnitude;
        if (magnitude < 1e-8 || !double.IsFinite(magnitude))
            return -96;
        return 20 * Math.Log10(magnitude);
    }

    static void Coefficients(
        double gainDb,
        double frequency,
        double sampleRate,
        out double b0,
        out double b1,
        out double b2,
        out double a1,
        out double a2)
    {
        var a = Math.Pow(10, gainDb / 40);
        var w0 = 2 * Math.PI * frequency / sampleRate;
        var alpha = Math.Sin(w0) / (2 * Q);
        var cos = Math.Cos(w0);
        var a0 = 1 + alpha / a;
        b0 = (1 + alpha * a) / a0;
        b1 = (-2 * cos) / a0;
        b2 = (1 - alpha * a) / a0;
        a1 = (-2 * cos) / a0;
        a2 = (1 - alpha / a) / a0;
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
