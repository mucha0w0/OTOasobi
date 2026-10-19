namespace Otoasobi;

sealed record OtoExport(
    double OffsetMs,
    double ConsonantMs,
    double Blank,
    IReadOnlyDictionary<string, double> Overlap,
    string Mode = "mono",
    double OverlapAt = 0.5,
    double RestOffsetMs = 0,
    double RestConsonantMs = 200,
    double RestBlank = 0,
    IReadOnlyDictionary<string, double>? RestOverlap = null,
    double VcOffsetMs = 0,
    IReadOnlyDictionary<string, double>? VcConsonant = null,
    IReadOnlyDictionary<string, double>? VcBlank = null,
    double VcOverlapAt = 0.5,
    double LeadOffsetMs = 0,
    double LeadConsonantMs = 200,
    double LeadBlank = 0,
    IReadOnlyDictionary<string, double>? LeadOverlap = null);
