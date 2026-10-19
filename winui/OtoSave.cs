using System.Diagnostics;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Otoasobi;

// 計算方法、スライダー、プリセット。開いているフォルダの直下に置く。
sealed class OtoSaveFile
{
    public const int CurrentVersion = 1;
    public const string KindId = "otoasobi-settings";

    public int Version { get; set; } = CurrentVersion;

    public string? Kind { get; set; }

    public string Name { get; set; } = "";

    public DateTimeOffset SavedAt { get; set; }

    // mono / renzoku / cvvc。画面の単独音、連続音、CVVC。
    public string Mode { get; set; } = "mono";

    public string StandalonePreset { get; set; } = OtoPreset.BuiltInName;

    public string RestPreset { get; set; } = OtoPreset.BuiltInName;

    public string CvvcPreset { get; set; } = "";

    public string LeadPreset { get; set; } = OtoPreset.BuiltInName;

    public OtoSliderState Standalone { get; set; } = new();

    public RenzokuSliderState Renzoku { get; set; } = new();

    public CvvcSliderState Cvvc { get; set; } = new();

    // null はプリセット一覧を触らない。作業中の状態は null のまま。
    public List<OtoPreset>? Presets { get; set; }

    public List<CvvcPreset>? CvvcPresets { get; set; }

    public EffectLibrary? Effects { get; set; }
}

sealed class OtoSliderState
{
    public double Offset { get; set; }

    public double Consonant { get; set; } = 0.2;

    public double Blank { get; set; }

    public Dictionary<string, double> Overlap { get; set; } = [];
}

sealed class RenzokuSliderState
{
    public double Offset { get; set; }

    public double Consonant { get; set; } = 0.2;

    public double Blank { get; set; }

    public double Overlap { get; set; } = 50;
}

sealed class CvvcSliderState
{
    public double Consonant { get; set; } = 0.2;

    public double Blank { get; set; }

    public Dictionary<string, double> Overlap { get; set; } = [];

    public double VcOffset { get; set; }

    public double VcOverlap { get; set; } = 50;

    public Dictionary<string, double> VcConsonant { get; set; } = [];

    public Dictionary<string, double> VcBlank { get; set; } = [];
}

sealed class EffectLibrary
{
    public List<StoredEffect> Chain { get; set; } = [];

    public List<EqPreset> EqPresets { get; set; } = [];

    public List<NormalizePreset> NormalizePresets { get; set; } = [];

    public List<HighPassPreset> HighPassPresets { get; set; } = [];

    public List<NoisePreset> NoisePresets { get; set; } = [];

    public List<CompressorPreset> CompressorPresets { get; set; } = [];

    public List<EffectRackPreset> RackPresets { get; set; } = [];
}

sealed class OtoSaveSummary
{
    public required string Name { get; init; }

    public required string Mode { get; init; }

    public required DateTimeOffset SavedAt { get; init; }

    public required string Path { get; init; }

    public string Detail
    {
        get
        {
            var mode = OtoSaveStore.ModeLabel(Mode);
            if (SavedAt.Year < 2000)
                return mode;
            var when = SavedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
            return mode + "  " + when;
        }
    }
}

static class OtoSaveStore
{
    public const string FilePrefix = "OTOasibi_";
    const string SessionStem = "作業中";
    const string CacheFileName = "OTOasibi_cache.json";
    const string SessionFileName = FilePrefix + SessionStem + ".json";

    static readonly string[] ReservedDevices =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ModeLabel(string? mode) => mode switch
    {
        "renzoku" => "連続音",
        "cvvc" => "CVVC",
        _ => "単独音",
    };

    public static string FilePathFor(string folder, string name) =>
        Path.Combine(folder, FilePrefix + name.Trim() + ".json");

    public static string SessionPath(string folder) => Path.Combine(folder, SessionFileName);

    public static string? NameError(string raw)
    {
        var name = raw.Trim();
        if (name.Length == 0)
            return "名前を入力してください。";
        if (name.Length > 40)
            return "名前は40文字までです。";
        if (name.Any(char.IsControl) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "名前に使えない文字があります。";
        if (name.EndsWith('.') || name.EndsWith(' '))
            return "名前に使えない文字があります。";
        if (name.Equals(SessionStem, StringComparison.OrdinalIgnoreCase)
            || name.Equals("cache", StringComparison.OrdinalIgnoreCase))
            return "その名前は使えません。";
        var stem = name;
        var dot = name.IndexOf('.');
        if (dot >= 0)
            stem = name[..dot];
        if (ReservedDevices.Any(device => stem.Equals(device, StringComparison.OrdinalIgnoreCase)))
            return "その名前は使えません。";
        return null;
    }

    public static IReadOnlyList<OtoSaveSummary> List(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return [];
            var result = new List<OtoSaveSummary>();
            foreach (var path in Directory.EnumerateFiles(folder, FilePrefix + "*.json"))
            {
                if (IsSession(path) || IsCache(path))
                    continue;
                OtoSaveFile file;
                try
                {
                    file = Read(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
                {
                    continue;
                }

                if (file.Name.Length == 0 || NameError(file.Name) != null)
                    continue;
                result.Add(new OtoSaveSummary
                {
                    Name = file.Name,
                    Mode = file.Mode,
                    SavedAt = file.SavedAt,
                    Path = path,
                });
            }

            result.Sort(static (left, right) =>
            {
                var byTime = right.SavedAt.CompareTo(left.SavedAt);
                if (byTime != 0)
                    return byTime;
                return string.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
            });
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static OtoSaveFile? TryLoadSession(string folder, out bool keepExisting)
    {
        keepExisting = false;
        try
        {
            var path = SessionPath(folder);
            if (!File.Exists(path))
                return null;
            return Read(path);
        }
        catch (InvalidDataException)
        {
            keepExisting = true;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static OtoSaveFile Read(string path)
    {
        var file = JsonSerializer.Deserialize<OtoSaveFile>(File.ReadAllText(path), JsonOptions)
            ?? throw new JsonException("空のファイルです。");
        if (file.Version > OtoSaveFile.CurrentVersion)
            throw new InvalidDataException("この保存データは、いまの OTOasobi では読めません。");
        if (file.Kind != OtoSaveFile.KindId)
            throw new InvalidDataException("このファイルは設定の保存データではありません。");
        if (!IsSession(path))
        {
            var name = DisplayName(path);
            if (string.IsNullOrEmpty(name))
                throw new InvalidDataException("このファイルは設定の保存データではありません。");
            file.Name = name;
        }

        return Clean(file);
    }

    public static void WriteNamed(string folder, OtoSaveFile file)
    {
        var name = file.Name.Trim();
        var error = NameError(name);
        if (error != null)
            throw new ArgumentException(error);
        file.Name = name;
        file.Kind = OtoSaveFile.KindId;
        file.Version = OtoSaveFile.CurrentVersion;
        file.SavedAt = DateTimeOffset.Now;
        WriteTo(FilePathFor(folder, name), file);
    }

    public static void WriteSession(string folder, OtoSaveFile file)
    {
        file.Name = "";
        file.Kind = OtoSaveFile.KindId;
        file.Version = OtoSaveFile.CurrentVersion;
        file.SavedAt = DateTimeOffset.Now;
        file.Presets = null;
        file.CvvcPresets = null;
        file.Effects = null;
        WriteTo(SessionPath(folder), file);
    }

    public static void Delete(string folder, string name)
    {
        var error = NameError(name);
        if (error != null)
            throw new ArgumentException(error);
        var path = FilePathFor(folder, name);
        if (File.Exists(path))
            File.Delete(path);
    }

    public static void OpenFolder(string folder)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + folder + "\"",
            UseShellExecute = true,
        });
    }

    static void WriteTo(string path, OtoSaveFile file)
    {
        var json = JsonSerializer.Serialize(file, JsonOptions);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    static bool IsSession(string path) =>
        Path.GetFileName(path).Equals(SessionFileName, StringComparison.OrdinalIgnoreCase);

    static bool IsCache(string path) =>
        Path.GetFileName(path).Equals(CacheFileName, StringComparison.OrdinalIgnoreCase);

    static string? DisplayName(string path)
    {
        var file = Path.GetFileName(path);
        if (!file.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase)
            || !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return null;
        var stem = file[..^".json".Length];
        if (stem.Length <= FilePrefix.Length)
            return null;
        return stem[FilePrefix.Length..];
    }

    static OtoSaveFile Clean(OtoSaveFile file)
    {
        file.Mode = file.Mode switch
        {
            "renzoku" => "renzoku",
            "cvvc" => "cvvc",
            _ => "mono",
        };
        file.StandalonePreset = PresetName(file.StandalonePreset, OtoPreset.BuiltInName);
        file.RestPreset = PresetName(file.RestPreset, OtoPreset.BuiltInName);
        file.CvvcPreset = PresetName(file.CvvcPreset, CvvcPreset.BuiltInName);
        file.LeadPreset = PresetName(file.LeadPreset, OtoPreset.BuiltInName);
        file.Standalone ??= new OtoSliderState();
        file.Renzoku ??= new RenzokuSliderState();
        file.Cvvc ??= new CvvcSliderState();
        CleanStandalone(file.Standalone);
        CleanRenzoku(file.Renzoku);
        CleanCvvc(file.Cvvc);
        if (file.Presets != null)
            file.Presets = OtoPresetStore.Normalize(file.Presets);
        if (file.CvvcPresets != null)
            file.CvvcPresets = CvvcPresetStore.Normalize(file.CvvcPresets);
        if (file.Effects != null)
            CleanEffects(file.Effects);
        return file;
    }

    static void CleanEffects(EffectLibrary library)
    {
        library.Chain = EffectRackPresetStore.NormalizeEffects(library.Chain);
        library.EqPresets = EqPresetStore.Normalize(library.EqPresets);
        library.NormalizePresets = NormalizePresetStore.Normalize(library.NormalizePresets);
        library.HighPassPresets = HighPassPresetStore.Normalize(library.HighPassPresets);
        library.NoisePresets = NoisePresetStore.Normalize(library.NoisePresets);
        library.CompressorPresets = CompressorPresetStore.Normalize(library.CompressorPresets);
        library.RackPresets = EffectRackPresetStore.Normalize(library.RackPresets);
    }

    static void CleanStandalone(OtoSliderState state)
    {
        state.Offset = Seconds(state.Offset, -0.1, 0.1, 0);
        state.Consonant = Seconds(state.Consonant, 0, 0.2, 0.2);
        state.Blank = Seconds(state.Blank, -0.1, 0.1, 0);
        state.Overlap = Overlaps(state.Overlap);
    }

    static void CleanRenzoku(RenzokuSliderState state)
    {
        state.Offset = Seconds(state.Offset, -0.1, 0.1, 0);
        state.Consonant = Seconds(state.Consonant, 0, 0.2, 0.2);
        state.Blank = Seconds(state.Blank, -0.1, 0.1, 0);
        state.Overlap = CvvcPresetStore.Percent(state.Overlap, 50);
    }

    static void CleanCvvc(CvvcSliderState state)
    {
        state.Consonant = Seconds(state.Consonant, 0, 0.2, 0.2);
        state.Blank = Seconds(state.Blank, -0.1, 0.1, 0);
        state.VcOffset = Seconds(state.VcOffset, -0.1, 0.1, 0);
        state.VcOverlap = CvvcPresetStore.Percent(state.VcOverlap, 50);
        state.Overlap = Percents(state.Overlap, 50);
        state.VcConsonant = Percents(state.VcConsonant, 100);
        state.VcBlank = Percents(state.VcBlank, 100);
    }

    static string PresetName(string? name, string fallback)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || name.Length > 40 || name.Any(char.IsControl))
            return fallback;
        return name;
    }

    static Dictionary<string, double> Overlaps(Dictionary<string, double>? table)
    {
        var result = new Dictionary<string, double>();
        if (table == null)
            return result;
        var known = KnownKeys();
        foreach (var (key, value) in table)
        {
            if (!known.Contains(key))
                continue;
            result[key] = ConsonantOverlap.Stored(value);
        }

        return result;
    }

    static Dictionary<string, double> Percents(Dictionary<string, double>? table, double fallback)
    {
        var result = new Dictionary<string, double>();
        if (table == null)
            return result;
        var known = KnownKeys();
        foreach (var (key, value) in table)
        {
            if (!known.Contains(key))
                continue;
            result[key] = CvvcPresetStore.Percent(value, fallback);
        }

        return result;
    }

    static HashSet<string>? _knownKeys;

    static HashSet<string> KnownKeys()
    {
        if (_knownKeys != null)
            return _knownKeys;
        _knownKeys = [];
        foreach (var (_, key, _) in ConsonantOverlap.Kinds)
            _knownKeys.Add(key);
        return _knownKeys;
    }

    static double Seconds(double value, double lo, double hi, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return fallback;
        return Math.Clamp(Math.Round(value, 3), lo, hi);
    }
}
