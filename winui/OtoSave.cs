using System.Diagnostics;
using System.Globalization;
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

    // 線で直した位置。モードごと。無いときは自動推定だけ。
    public OtoAdjustBook? Adjustments { get; set; }
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

// 音素ごとの線の位置。null の欄は自動推定のまま。時刻はファイル先頭からの秒。
sealed class OtoAdjustedEntry
{
    public string File { get; set; } = "";

    public int Index { get; set; }

    public string Alias { get; set; } = "";

    // 手で直したエイリアス。null は自動の名前。編集印の * は入れない。
    public string? Name { get; set; }

    public double? Offset { get; set; }

    public double? Overlap { get; set; }

    public double? Preutter { get; set; }

    public double? Consonant { get; set; }

    public double? Blank { get; set; }

    public bool Any =>
        Offset != null || Overlap != null || Preutter != null || Consonant != null || Blank != null;

    public double? Point(int point) => point switch
    {
        0 => Offset,
        1 => Overlap,
        2 => Preutter,
        3 => Consonant,
        _ => Blank,
    };

    public void SetPoint(int point, double? value)
    {
        switch (point)
        {
            case 0:
                Offset = value;
                break;
            case 1:
                Overlap = value;
                break;
            case 2:
                Preutter = value;
                break;
            case 3:
                Consonant = value;
                break;
            default:
                Blank = value;
                break;
        }
    }

    public double?[] Points() => [Offset, Overlap, Preutter, Consonant, Blank];

    public OtoAdjustedEntry Copy() => new()
    {
        File = File,
        Index = Index,
        Alias = Alias,
        Name = Name,
        Offset = Offset,
        Overlap = Overlap,
        Preutter = Preutter,
        Consonant = Consonant,
        Blank = Blank,
    };
}

sealed class OtoAdjustBook
{
    public List<OtoAdjustedEntry> Mono { get; set; } = [];

    public List<OtoAdjustedEntry> Renzoku { get; set; } = [];

    public List<OtoAdjustedEntry> Cvvc { get; set; } = [];

    public bool IsEmpty => Mono.Count == 0 && Renzoku.Count == 0 && Cvvc.Count == 0;

    public List<OtoAdjustedEntry> For(string? mode) => mode switch
    {
        "renzoku" => Renzoku,
        "cvvc" => Cvvc,
        _ => Mono,
    };

    public OtoAdjustBook Copy() => new()
    {
        Mono = Mono.Select(entry => entry.Copy()).ToList(),
        Renzoku = Renzoku.Select(entry => entry.Copy()).ToList(),
        Cvvc = Cvvc.Select(entry => entry.Copy()).ToList(),
    };

    public void Remember(string? mode, OtoAdjustedEntry entry)
    {
        var list = For(mode);
        string? keptName = entry.Name;
        if (string.IsNullOrEmpty(keptName))
        {
            foreach (var item in list)
            {
                if (item.Index != entry.Index || !item.File.Equals(entry.File, StringComparison.OrdinalIgnoreCase))
                    continue;
                keptName = item.Name;
                break;
            }
        }

        list.RemoveAll(item =>
            item.Index == entry.Index
            && item.File.Equals(entry.File, StringComparison.OrdinalIgnoreCase));
        var stored = entry.Copy();
        stored.Name = string.IsNullOrEmpty(keptName) ? null : keptName;
        if (stored.Any || !string.IsNullOrEmpty(stored.Name))
            list.Add(stored);
    }

    public void RememberName(string? mode, string file, int index, string autoAlias, string? name)
    {
        var list = For(mode);
        var cleaned = string.IsNullOrEmpty(name) ? null : name;
        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            if (item.Index != index || !item.File.Equals(file, StringComparison.OrdinalIgnoreCase))
                continue;
            item.Name = cleaned;
            if (!string.IsNullOrEmpty(autoAlias))
                item.Alias = autoAlias;
            if (!item.Any && string.IsNullOrEmpty(item.Name))
                list.RemoveAt(i);
            return;
        }

        if (cleaned == null)
            return;
        list.Add(new OtoAdjustedEntry
        {
            File = file,
            Index = index,
            Alias = autoAlias,
            Name = cleaned,
        });
    }
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
    public const string CacheFileName = "OTOasibi_cache.json";
    const string SessionFileName = FilePrefix + SessionStem + ".json";

    static readonly string[] ReservedDevices =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

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
        var basic = PresetUi.BasicNameError(raw);
        if (basic != null)
            return basic;
        var name = raw.Trim();
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
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
        var file = JsonSerializer.Deserialize<OtoSaveFile>(File.ReadAllText(path), JsonFiles.OptionsIgnoringNull)
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

    static void WriteTo(string path, OtoSaveFile file) =>
        JsonFiles.Write(path, file, JsonFiles.OptionsIgnoringNull);

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
        file.Adjustments = CleanAdjust(file.Adjustments);
        return file;
    }

    static OtoAdjustBook? CleanAdjust(OtoAdjustBook? book)
    {
        if (book == null)
            return null;
        var cleaned = new OtoAdjustBook
        {
            Mono = CleanEntries(book.Mono),
            Renzoku = CleanEntries(book.Renzoku),
            Cvvc = CleanEntries(book.Cvvc),
        };
        return cleaned.IsEmpty ? null : cleaned;
    }

    static List<OtoAdjustedEntry> CleanEntries(List<OtoAdjustedEntry>? list)
    {
        var result = new List<OtoAdjustedEntry>();
        if (list == null)
            return result;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = list.Count - 1; i >= 0; i--)
        {
            var entry = list[i];
            if (entry == null)
                continue;
            var file = (entry.File ?? "").Trim();
            var alias = (entry.Alias ?? "").Trim();
            if (file.Length == 0 || file.Length > 255 || alias.Length == 0 || alias.Length > 80)
                continue;
            if (entry.Index < 0 || entry.Index > 10000)
                continue;
            var key = file + "\n" + entry.Index.ToString(CultureInfo.InvariantCulture);
            if (!seen.Add(key))
                continue;
            entry.File = file;
            entry.Alias = alias;
            entry.Name = OtoIni.CleanAlias(entry.Name);
            entry.Offset = CleanTime(entry.Offset);
            entry.Overlap = CleanTime(entry.Overlap);
            entry.Preutter = CleanTime(entry.Preutter);
            entry.Consonant = CleanTime(entry.Consonant);
            entry.Blank = CleanTime(entry.Blank);
            if (!entry.Any && string.IsNullOrEmpty(entry.Name))
                continue;
            result.Add(entry);
        }

        result.Reverse();
        return result;
    }

    static double? CleanTime(double? value)
    {
        if (value is not double time || double.IsNaN(time) || double.IsInfinity(time))
            return null;
        return Math.Clamp(Math.Round(time, 6), -0.1, 36000);
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
