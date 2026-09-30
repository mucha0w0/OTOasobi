namespace Otoasobi;

public sealed class PhoneSpan
{
    public double Start { get; set; }
    public double End { get; set; }
    public string Label { get; set; } = "";
    public string Token { get; set; } = "";
}

public sealed class WavFile
{
    public string Name { get; set; } = "";
    public double Duration { get; set; }
    public string Engine { get; set; } = "";
    public List<PhoneSpan> Spans { get; } = [];
}

sealed record FolderData(bool Ok, string? Error, string? Notice, IReadOnlyList<WavFile> Files, bool Cached);

sealed record PreviewData(
    bool Ok,
    string? Error,
    double[] Lo,
    double[] Hi,
    double[] Loudness,
    byte[] Png,
    double SpecT0,
    double SpecT1,
    double WaveT0,
    double WaveT1,
    bool Samples,
    int SampleRate,
    double Duration);
