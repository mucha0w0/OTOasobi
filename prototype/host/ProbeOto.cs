using System.Text.Json;
using Otoasobi;

namespace Otoasobi.Probe;

// スライダー、プリセット、oto.ini の材料。数値の計算は製品の OtoMarks と OtoExport。
sealed class ProbeOto
{
    const double DefaultOverlap = 50;
    const double DefaultVcConsonant = 100;
    const double DefaultVcBlank = 100;

    readonly List<WavFile> _files = [];
    readonly OtoSliderState _mono = new();
    readonly Dictionary<string, double> _overlap = [];
    readonly RenzokuSliderState _renzoku = new();
    readonly CvvcSliderState _cvvc = new();
    readonly List<OtoPreset> _presets = [];
    readonly List<CvvcPreset> _cvvcPresets = [];
    string? _folder;
    bool _locked;
    string _mode = "mono";
    string _preset = OtoPreset.BuiltInName;
    string _rest = OtoPreset.BuiltInName;
    string _lead = OtoPreset.BuiltInName;
    string _cvvcName = CvvcPreset.BuiltInName;
    bool _aliasMonoWanted = true;
    bool _aliasRenzoku;
    bool _aliasCvvc;
    string _scope = "all";
    bool _busy;
    readonly OtoAdjustBook _adjust = new();
    readonly object _gate = new();
    string? _loudPath;
    double[]? _loudness;

    public bool Busy => _busy;

    public bool SessionLocked => _locked;

    public bool Analyzed => _files.Count > 0;

    public bool IsBusy => _busy;

    public void Open(string folder)
    {
        lock (_gate)
            OpenUnlocked(folder);
    }

    void OpenUnlocked(string folder)
    {
        if (!string.IsNullOrEmpty(_folder) && !string.Equals(_folder, folder, StringComparison.OrdinalIgnoreCase))
            FlushSession();
        _folder = folder;
        _files.Clear();
        _loudPath = null;
        _loudness = null;
        ReloadLibraries();
        var session = OtoSaveStore.TryLoadSession(folder, out var keep);
        if (session != null)
        {
            _locked = false;
            Apply(session);
        }
        else if (!keep)
        {
            _locked = false;
            Apply(new OtoSaveFile());
        }
        else
        {
            _locked = true;
        }
    }

    public void FlushSession()
    {
        if (_locked || string.IsNullOrEmpty(_folder))
            return;
        try
        {
            OtoSaveStore.WriteSession(_folder, Capture(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    public void SetBusy(bool busy) => _busy = busy;

    public void SetFiles(IReadOnlyList<WavFile> files)
    {
        lock (_gate)
        {
            _files.Clear();
            _files.AddRange(files);
        }
    }

    public void MergeFile(WavFile file)
    {
        lock (_gate)
        {
            var index = _files.FindIndex(item => string.Equals(item.Name, file.Name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                _files.Add(file);
            else
                _files[index] = file;
        }
    }

    public object[] PreutterClears()
    {
        var map = new Dictionary<string, List<double[]>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in _files)
        {
            foreach (var mark in Marks(file.Name))
            {
                if (double.IsNaN(mark.Offset) || double.IsNaN(mark.Preutter) || mark.Preutter <= mark.Offset)
                    continue;
                if (!map.TryGetValue(file.Name, out var ranges))
                {
                    ranges = [];
                    map[file.Name] = ranges;
                }

                ranges.Add([mark.Offset, mark.Preutter]);
            }
        }

        return map.Select(pair => new { name = pair.Key, ranges = pair.Value }).ToArray();
    }

    public IReadOnlyList<WavFile> Files => _files;

    public bool SetSlider(string? group, string? key, double value)
    {
        lock (_gate)
            return SetSliderUnlocked(group, key, value);
    }

    bool SetSliderUnlocked(string? group, string? key, double value)
    {
        if (string.IsNullOrEmpty(group) || string.IsNullOrEmpty(key))
            return false;
        switch (group)
        {
            case "mono":
                if (key == "offset")
                    _mono.Offset = Seconds(value, -0.1, 0.1, 0);
                else if (key == "consonant")
                    _mono.Consonant = Seconds(value, 0, 0.2, 0.2);
                else if (key == "blank")
                    _mono.Blank = Seconds(value, -0.1, 0.1, 0);
                else
                    return false;
                return true;
            case "monoOverlap":
                if (!Known(key))
                    return false;
                _overlap[key] = ConsonantOverlap.Stored(value);
                return true;
            case "renzoku":
                if (key == "offset")
                    _renzoku.Offset = Seconds(value, -0.1, 0.1, 0);
                else if (key == "consonant")
                    _renzoku.Consonant = Seconds(value, 0, 0.2, 0.2);
                else if (key == "blank")
                    _renzoku.Blank = Seconds(value, -0.1, 0.1, 0);
                else if (key == "overlap")
                    _renzoku.Overlap = CvvcPresetStore.Percent(value, DefaultOverlap);
                else
                    return false;
                return true;
            case "cvvc":
                if (key == "consonant")
                    _cvvc.Consonant = Seconds(value, 0, 0.2, 0.2);
                else if (key == "blank")
                    _cvvc.Blank = Seconds(value, -0.1, 0.1, 0);
                else if (key == "vcOffset")
                    _cvvc.VcOffset = Seconds(value, -0.1, 0.1, 0);
                else if (key == "vcOverlap")
                    _cvvc.VcOverlap = CvvcPresetStore.Percent(value, DefaultOverlap);
                else
                    return false;
                return true;
            case "cvvcOverlap":
                return PutPercent(_cvvc.Overlap, key, value, DefaultOverlap);
            case "vcConsonant":
                return PutPercent(_cvvc.VcConsonant, key, value, DefaultVcConsonant);
            case "vcBlank":
                return PutPercent(_cvvc.VcBlank, key, value, DefaultVcBlank);
            default:
                return false;
        }
    }

    public void SetMode(string? mode)
    {
        _mode = mode switch
        {
            "renzoku" => "renzoku",
            "cvvc" => "cvvc",
            _ => "mono",
        };
    }

    public void SelectPreset(string? target, string? name)
    {
        if (target == "cvvc")
        {
            var preset = FindCvvc(name);
            _cvvcName = preset.Name;
            ApplyCvvc(preset);
            return;
        }

        var oto = FindOto(name);
        if (target == "rest")
        {
            _rest = oto.Name;
            _lead = oto.Name;
        }
        else if (target == "lead")
        {
            _lead = oto.Name;
            _rest = oto.Name;
        }
        else
        {
            _preset = oto.Name;
            ApplyMono(oto);
        }
    }

    public string? SavePreset(string? which, string? raw)
    {
        try
        {
            if (which == "cvvc")
                return SaveCvvc(raw);
            var error = PresetUi.ReservedNameError(raw ?? "", name => name == OtoPreset.BuiltInName, "初期値という名前は使えません。");
            if (error != null)
                return error;
            var name = (raw ?? "").Trim();
            var existing = _presets.FirstOrDefault(preset => preset.Name == name);
            if (existing == null)
            {
                existing = new OtoPreset { Name = name };
                _presets.Add(existing);
            }

            CopyMono(existing);
            _preset = name;
            OtoPresetStore.Save(_presets);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return ex.Message;
        }
    }

    public void SetAlias(string? which, bool on)
    {
        if (which == "renzoku")
            _aliasRenzoku = on;
        else if (which == "cvvc")
            _aliasCvvc = on;
        else if (!_aliasRenzoku && !_aliasCvvc)
            _aliasMonoWanted = on;
    }

    public void SetScope(string? scope)
    {
        if (!HasIni)
        {
            _scope = "all";
            return;
        }

        _scope = scope switch
        {
            "missing" => "missing",
            "overlap" => "overlap",
            _ => "all",
        };
    }

    public List<string> AliasModes()
    {
        var modes = new List<string>();
        if (_aliasMonoWanted && !_aliasRenzoku && !_aliasCvvc)
            modes.Add("mono");
        if (_aliasRenzoku)
            modes.Add("renzoku");
        if (_aliasCvvc)
            modes.Add("cvvc");
        return modes;
    }

    public string ScopeForWrite() => HasIni ? _scope : "all";

    public OtoExport ExportFor(string mode)
    {
        if (mode == "cvvc")
            return CvvcExport();
        if (mode == "renzoku")
            return RenzokuExport();
        return MonoExport();
    }

    public string? SaveNamed(string? raw, bool replace)
    {
        if (string.IsNullOrEmpty(_folder))
            return "フォルダを開いてください。";
        var error = OtoSaveStore.NameError(raw ?? "");
        if (error != null)
            return error;
        var name = (raw ?? "").Trim();
        var path = OtoSaveStore.FilePathFor(_folder, name);
        if (!replace && File.Exists(path))
            return "exists";
        try
        {
            var file = Capture(true);
            file.Name = name;
            OtoSaveStore.WriteNamed(_folder, file);
            FlushSession();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return ex.Message;
        }
    }

    public string? LoadNamed(string? name)
    {
        if (string.IsNullOrEmpty(_folder))
            return "フォルダを開いてください。";
        var error = OtoSaveStore.NameError(name ?? "");
        if (error != null)
            return error;
        try
        {
            Apply(OtoSaveStore.Read(OtoSaveStore.FilePathFor(_folder, (name ?? "").Trim())));
            FlushSession();
            return null;
        }
        catch (InvalidDataException ex)
        {
            return ex.Message;
        }
        catch (JsonException)
        {
            return "ファイルの内容を読めませんでした。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return ex.Message;
        }
    }

    public object Snapshot(string? fileName, bool includeRows, int generation, int rowGeneration = 0)
    {
        lock (_gate)
            return new
        {
            type = "oto",
            generation,
            rowGeneration,
            mode = _mode,
            busy = _busy,
            analyzed = _files.Count > 0,
            hasIni = HasIni,
            scope = HasIni ? _scope : "all",
            aliasMono = _aliasRenzoku || _aliasCvvc || _aliasMonoWanted,
            aliasMonoLocked = _aliasRenzoku || _aliasCvvc,
            aliasRenzoku = _aliasRenzoku,
            aliasCvvc = _aliasCvvc,
            presets = new[] { OtoPreset.BuiltInName }.Concat(_presets.Select(preset => preset.Name)).ToArray(),
            cvvcPresets = new[] { CvvcPreset.BuiltInName }.Concat(_cvvcPresets.Select(preset => preset.Name)).ToArray(),
            saves = Saves(),
            standalonePreset = _preset,
            standaloneDirty = MonoDirty(),
            restPreset = _rest,
            leadPreset = _lead,
            cvvcPreset = _cvvcName,
            cvvcDirty = CvvcDirty(),
            offset = _mono.Offset,
            consonant = _mono.Consonant,
            blank = _mono.Blank,
            overlap = Shown(_overlap, DefaultOverlap),
            renzokuOffset = _renzoku.Offset,
            renzokuConsonant = _renzoku.Consonant,
            renzokuBlank = _renzoku.Blank,
            renzokuOverlap = _renzoku.Overlap,
            cvvcConsonant = _cvvc.Consonant,
            cvvcBlank = _cvvc.Blank,
            cvvcOverlap = Shown(_cvvc.Overlap, DefaultOverlap),
            vcOffset = _cvvc.VcOffset,
            vcOverlap = _cvvc.VcOverlap,
            vcConsonant = Shown(_cvvc.VcConsonant, DefaultVcConsonant),
            vcBlank = Shown(_cvvc.VcBlank, DefaultVcBlank),
            kinds = ConsonantOverlap.Kinds.Select(kind => new { group = kind.Group, key = kind.Key, caption = kind.Caption }).ToArray(),
            spans = SpansOf(fileName),
            marks = FileMarks(fileName),
            rows = includeRows ? Rows() : null,
        };
    }

    public List<OtoAdjustedEntry> Adjustments(string mode) =>
        _adjust.For(mode).Select(entry => entry.Copy()).ToList();

    public bool TunePoint(string? fileName, int index, int point, double time)
    {
        if (!HasIni)
            return false;
        var file = FindFile(fileName);
        if (file == null || point < 0 || point >= OtoTune.PointCount)
            return false;
        var marks = IniMarksForFile(file);
        if (index < 0 || index >= marks.Count)
            return false;
        var basis = marks[index];
        var duration = Math.Max(file.Duration, 0);
        var current = basis;
        var stack = new bool[OtoTune.PointCount];
        stack[point] = true;
        var next = new double?[OtoTune.PointCount];
        next[point] = OtoTune.ClampGroup(current, stack, time, duration);
        var placed = OtoTune.Apply(basis, next, duration);
        return OtoIniFile.TryPatchLine(_folder, file.Name, index, placed, basis.Alias);
    }

    public bool MoveSpans(string? fileName, IReadOnlyList<(double Start, double End)> times)
    {
        var file = FindFile(fileName);
        if (file == null || times.Count == 0 || times.Count != file.Spans.Count)
            return false;
        var duration = Math.Max(file.Duration, 0);
        foreach (var span in file.Spans)
            duration = Math.Max(duration, span.End);
        foreach (var (start, end) in times)
        {
            if (!double.IsFinite(start) || !double.IsFinite(end))
                return false;
            if (start < -1e-4 || end > duration + 1e-4 || end - start < 1e-6)
                return false;
        }

        for (var i = 0; i < times.Count; i++)
        {
            file.Spans[i].Start = times[i].Start;
            file.Spans[i].End = times[i].End;
        }

        return true;
    }

    public bool TuneFields(string? fileName, int index, string? alias, double offset, double overlap, double preutter, double consonant, double blank, string? mode = null)
    {
        var file = FindFile(fileName);
        if (file == null)
            return false;
        var which = Which(mode);
        var auto = MarksFor(file, which);
        if (index < 0 || index >= auto.Count || !string.Equals(auto[index].Alias, alias, StringComparison.Ordinal))
            return false;
        var desired = OtoIni.FromFields(auto[index], offset, overlap, preutter, consonant, blank);
        var placed = OtoTune.Place(
            auto[index],
            desired.Offset,
            desired.Overlap,
            desired.Preutter,
            desired.Consonant,
            desired.Blank,
            [true, true, true, true, true],
            Math.Max(file.Duration, 0));
        return StorePlaced(file, index, auto[index], placed, which);
    }

    public bool TuneAlias(string? fileName, int index, string? alias, string? name, string? mode = null)
    {
        var file = FindFile(fileName);
        if (file == null)
            return false;
        var which = Which(mode);
        var drafts = TableDrafts();
        var at = drafts.FindIndex(item =>
            item.File.Equals(file.Name, StringComparison.OrdinalIgnoreCase)
            && item.Index == index
            && item.AutoAlias == alias
            && item.Mode == which);
        if (at < 0)
            return false;
        var cleaned = OtoIni.CleanAlias(name);
        if (cleaned == null && !string.IsNullOrWhiteSpace(name))
            return false;
        var aliases = drafts.Select(item => item.AutoAlias).ToArray();
        var names = drafts.Select(item => item.Name).ToArray();
        names[at] = null;
        var natural = OtoIni.ResolveAliases(aliases, names)[at];
        var stored = cleaned != null && string.Equals(cleaned, natural, StringComparison.Ordinal) ? null : cleaned;
        var row = drafts[at];
        _adjust.RememberName(which, row.File, row.Index, row.AutoAlias, stored);
        return true;
    }

    public bool TuneComment(string? fileName, int index, string? alias, string? comment, string? mode = null)
    {
        var file = FindFile(fileName);
        if (file == null)
            return false;
        var which = Which(mode);
        var auto = MarksFor(file, which);
        if (index < 0 || index >= auto.Count || !string.Equals(auto[index].Alias, alias, StringComparison.Ordinal))
            return false;
        _adjust.RememberComment(which, file.Name, index, auto[index].Alias, OtoIni.CleanComment(comment));
        return true;
    }

    bool StorePlaced(WavFile file, int index, OtoMark auto, OtoMark placed, string mode)
    {
        var stored = new OtoAdjustedEntry
        {
            File = file.Name,
            Index = index,
            Alias = auto.Alias,
        };
        for (var point = 0; point < OtoTune.PointCount; point++)
        {
            var at = OtoTune.RoundSeconds(placed.At(point));
            stored.SetPoint(point, Math.Abs(at - auto.At(point)) <= OtoTune.MagnetSeconds ? null : at);
        }

        _adjust.Remember(mode, stored);
        return true;
    }

    object[] Rows()
    {
        var drafts = TableDrafts();
        var aliases = new string[drafts.Count];
        var names = new string?[drafts.Count];
        for (var i = 0; i < drafts.Count; i++)
        {
            aliases[i] = drafts[i].AutoAlias;
            names[i] = drafts[i].Name;
        }

        var numbered = OtoIni.ResolveAliases(aliases, names);
        var views = new object[drafts.Count];
        for (var i = 0; i < drafts.Count; i++)
        {
            var draft = drafts[i];
            views[i] = new
            {
                file = draft.File,
                index = draft.Index,
                mode = draft.Mode,
                alias = numbered[i],
                autoAlias = draft.AutoAlias,
                name = draft.Name ?? "",
                comment = draft.Comment,
                tuned = draft.Tuned,
                offset = draft.Shown.Offset,
                overlap = draft.Shown.Overlap,
                preutter = draft.Shown.Preutter,
                consonant = draft.Shown.Consonant,
                blank = draft.Shown.Blank,
            };
        }

        return views;
    }

    List<TableDraft> TableDrafts()
    {
        var drafts = new List<TableDraft>();
        if (string.IsNullOrEmpty(_folder))
            return drafts;
        var dedupeLeading = _aliasRenzoku && _aliasCvvc;
        foreach (var file in _files)
        {
            var leading = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var mode in AliasModes())
            {
                var auto = MarksFor(file, mode);
                for (var i = 0; i < auto.Count; i++)
                {
                    var alias = auto[i].Alias;
                    if (dedupeLeading && OtoIni.IsLeadingHyphenAlias(alias))
                    {
                        if (leading.TryGetValue(alias, out var at))
                        {
                            if (mode == "cvvc")
                                drafts[at] = Draft(file, mode, i, auto[i]);
                            continue;
                        }

                        leading[alias] = drafts.Count;
                    }

                    drafts.Add(Draft(file, mode, i, auto[i]));
                }
            }
        }

        return drafts;
    }

    TableDraft Draft(WavFile file, string mode, int index, OtoMark auto)
    {
        var entry = Match(_adjust.For(mode), file.Name, index, auto.Alias);
        var points = entry?.Points();
        var shown = OtoTune.Apply(auto, points, Math.Max(file.Duration, 0));
        var tuned = new bool[OtoTune.PointCount];
        if (points != null)
        {
            for (var point = 0; point < tuned.Length; point++)
                tuned[point] = points[point] != null;
        }

        var custom = string.IsNullOrEmpty(entry?.Name) ? null : entry!.Name;
        return new TableDraft(file.Name, mode, index, auto.Alias, custom, entry?.Comment ?? "", shown, tuned);
    }

    string Which(string? mode) => mode is "mono" or "renzoku" or "cvvc" ? mode : _mode;

    List<OtoMark> MarksFor(WavFile file, string mode)
    {
        var marks = new List<OtoMark>();
        if (!string.IsNullOrEmpty(_folder))
            OtoMarks.Fill(file, ExportFor(mode), marks, Loudness(Path.Combine(_folder, file.Name)));
        return marks;
    }

    object[] FileMarks(string? fileName)
    {
        var file = FindFile(fileName);
        if (file == null || !HasIni)
            return [];
        var marks = IniMarksForFile(file);
        var views = new object[marks.Count];
        for (var i = 0; i < marks.Count; i++)
            views[i] = ShownIni(file, i, marks[i]).View;
        return views;
    }

    ShownRow Shown(WavFile file, int index, OtoMark auto)
    {
        var entry = Match(_adjust.For(_mode), file.Name, index, auto.Alias);
        var points = entry?.Points();
        var shown = OtoTune.Apply(auto, points, Math.Max(file.Duration, 0));
        var tuned = new bool[OtoTune.PointCount];
        if (points != null)
        {
            for (var point = 0; point < tuned.Length; point++)
                tuned[point] = points[point] != null;
        }

        var any = tuned.Any(flag => flag);
        var custom = string.IsNullOrEmpty(entry?.Name) ? null : entry!.Name;
        var comment = entry?.Comment ?? "";
        var label = custom ?? auto.Alias;
        if (any || custom != null)
            label += " *";
        return new ShownRow(file.Name, index, auto.Alias, custom, new
        {
            file = file.Name,
            index,
            alias = label,
            autoAlias = auto.Alias,
            name = custom ?? "",
            comment,
            tuned,
            offset = shown.Offset,
            overlap = shown.Overlap,
            preutter = shown.Preutter,
            consonant = shown.Consonant,
            blank = shown.Blank,
        });
    }

    WavFile? FindFile(string? fileName) =>
        string.IsNullOrEmpty(fileName)
            ? null
            : _files.FirstOrDefault(item => string.Equals(item.Name, fileName, StringComparison.OrdinalIgnoreCase));

    List<OtoMark> IniMarksForFile(WavFile file) =>
        string.IsNullOrEmpty(_folder) ? [] : OtoIniFile.MarksForFile(_folder, file.Name);

    List<OtoMark> AutoMarks(WavFile file) => MarksFor(file, _mode);

    ShownRow ShownIni(WavFile file, int index, OtoMark mark) => new(
        file.Name,
        index,
        mark.Alias,
        null,
        new
        {
            file = file.Name,
            index,
            alias = mark.Alias,
            autoAlias = mark.Alias,
            name = "",
            comment = "",
            tuned = new bool[OtoTune.PointCount],
            offset = mark.Offset,
            overlap = mark.Overlap,
            preutter = mark.Preutter,
            consonant = mark.Consonant,
            blank = mark.Blank,
        });

    static OtoAdjustedEntry? Match(List<OtoAdjustedEntry> list, string file, int index, string alias)
    {
        foreach (var entry in list)
        {
            if (entry.Index == index && entry.File.Equals(file, StringComparison.OrdinalIgnoreCase) && entry.Alias == alias)
                return entry;
        }

        return null;
    }

    sealed record ShownRow(string File, int Index, string AutoAlias, string? Name, object View);

    sealed record TableDraft(string File, string Mode, int Index, string AutoAlias, string? Name, string Comment, OtoMark Shown, bool[] Tuned);

    object[] SpansOf(string? fileName)
    {
        var file = string.IsNullOrEmpty(fileName)
            ? null
            : _files.FirstOrDefault(item => string.Equals(item.Name, fileName, StringComparison.OrdinalIgnoreCase));
        if (file == null)
            return [];
        return file.Spans.Select(span => new { start = span.Start, end = span.End, label = span.Label, token = span.Token }).ToArray();
    }

    public bool HasIni => OtoIniFile.Exists(_folder);

    void ReloadLibraries()
    {
        _presets.Clear();
        _presets.AddRange(OtoPresetStore.Load());
        _cvvcPresets.Clear();
        _cvvcPresets.AddRange(CvvcPresetStore.Load());
    }

    void Apply(OtoSaveFile file)
    {
        if (file.Presets != null)
        {
            _presets.Clear();
            _presets.AddRange(file.Presets.Where(preset => !preset.BuiltIn));
            try
            {
                OtoPresetStore.Save(_presets);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        if (file.CvvcPresets != null)
        {
            _cvvcPresets.Clear();
            _cvvcPresets.AddRange(file.CvvcPresets.Where(preset => !preset.BuiltIn));
            try
            {
                CvvcPresetStore.Save(_cvvcPresets);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        _mode = file.Mode;
        _preset = FindOto(file.StandalonePreset).Name;
        _rest = FindOto(file.RestPreset).Name;
        _lead = FindOto(file.LeadPreset).Name;
        _cvvcName = FindCvvc(file.CvvcPreset).Name;
        _mono.Offset = file.Standalone.Offset;
        _mono.Consonant = file.Standalone.Consonant;
        _mono.Blank = file.Standalone.Blank;
        _overlap.Clear();
        foreach (var (key, value) in file.Standalone.Overlap)
            _overlap[key] = value;
        _renzoku.Offset = file.Renzoku.Offset;
        _renzoku.Consonant = file.Renzoku.Consonant;
        _renzoku.Blank = file.Renzoku.Blank;
        _renzoku.Overlap = file.Renzoku.Overlap;
        _cvvc.Consonant = file.Cvvc.Consonant;
        _cvvc.Blank = file.Cvvc.Blank;
        _cvvc.VcOffset = file.Cvvc.VcOffset;
        _cvvc.VcOverlap = file.Cvvc.VcOverlap;
        Replace(_cvvc.Overlap, file.Cvvc.Overlap);
        Replace(_cvvc.VcConsonant, file.Cvvc.VcConsonant);
        Replace(_cvvc.VcBlank, file.Cvvc.VcBlank);
        IReadOnlyList<string> aliases = file.Aliases ?? file.Mode switch
        {
            "cvvc" => ["cvvc"],
            "renzoku" => ["renzoku"],
            _ => ["mono"],
        };
        _aliasMonoWanted = aliases.Contains("mono");
        _aliasRenzoku = aliases.Contains("renzoku");
        _aliasCvvc = aliases.Contains("cvvc");
        LoadAdjust(file.Adjustments);
    }

    void LoadAdjust(OtoAdjustBook? book)
    {
        _adjust.Mono.Clear();
        _adjust.Renzoku.Clear();
        _adjust.Cvvc.Clear();
        if (book == null)
            return;
        _adjust.Mono.AddRange(book.Mono.Select(entry => entry.Copy()));
        _adjust.Renzoku.AddRange(book.Renzoku.Select(entry => entry.Copy()));
        _adjust.Cvvc.AddRange(book.Cvvc.Select(entry => entry.Copy()));
    }

    OtoSaveFile Capture(bool libraries)
    {
        var file = new OtoSaveFile
        {
            Version = OtoSaveFile.CurrentVersion,
            Kind = OtoSaveFile.KindId,
            Mode = _mode,
            Aliases = CaptureAliases(),
            StandalonePreset = _preset,
            RestPreset = _rest,
            CvvcPreset = _cvvcName,
            LeadPreset = _lead,
            Standalone = new OtoSliderState
            {
                Offset = Math.Round(_mono.Offset, 3),
                Consonant = Math.Round(_mono.Consonant, 3),
                Blank = Math.Round(_mono.Blank, 3),
                Overlap = new Dictionary<string, double>(_overlap),
            },
            Renzoku = new RenzokuSliderState
            {
                Offset = Math.Round(_renzoku.Offset, 3),
                Consonant = Math.Round(_renzoku.Consonant, 3),
                Blank = Math.Round(_renzoku.Blank, 3),
                Overlap = _renzoku.Overlap,
            },
            Adjustments = _adjust.IsEmpty ? null : _adjust.Copy(),
            Cvvc = new CvvcSliderState
            {
                Consonant = Math.Round(_cvvc.Consonant, 3),
                Blank = Math.Round(_cvvc.Blank, 3),
                VcOffset = Math.Round(_cvvc.VcOffset, 3),
                VcOverlap = _cvvc.VcOverlap,
                Overlap = new Dictionary<string, double>(_cvvc.Overlap),
                VcConsonant = new Dictionary<string, double>(_cvvc.VcConsonant),
                VcBlank = new Dictionary<string, double>(_cvvc.VcBlank),
            },
        };
        if (!libraries)
            return file;
        file.Presets = _presets.ToList();
        file.CvvcPresets = _cvvcPresets.ToList();
        return file;
    }

    List<string> CaptureAliases()
    {
        var list = new List<string>();
        if (_aliasMonoWanted)
            list.Add("mono");
        if (_aliasRenzoku)
            list.Add("renzoku");
        if (_aliasCvvc)
            list.Add("cvvc");
        return list;
    }

    object[] Saves()
    {
        if (string.IsNullOrEmpty(_folder))
            return [];
        return OtoSaveStore.List(_folder)
            .Select(item => new { name = item.Name, mode = OtoSaveStore.ModeLabel(item.Mode) })
            .ToArray();
    }

    List<OtoMark> Marks(string? fileName)
    {
        var marks = new List<OtoMark>();
        if (string.IsNullOrEmpty(fileName))
            return marks;
        var file = _files.FirstOrDefault(item => string.Equals(item.Name, fileName, StringComparison.OrdinalIgnoreCase));
        if (file == null || string.IsNullOrEmpty(_folder))
            return marks;
        OtoMarks.Fill(file, ExportFor(_mode), marks, Loudness(Path.Combine(_folder, file.Name)));
        var duration = Math.Max(file.Duration, 0);
        for (var i = 0; i < marks.Count; i++)
        {
            var entry = Match(_adjust.For(_mode), file.Name, i, marks[i].Alias);
            marks[i] = OtoTune.Apply(marks[i], entry?.Points(), duration);
        }

        return marks;
    }

    double[]? Loudness(string path)
    {
        if (_loudPath == path)
            return _loudness;
        _loudPath = path;
        try
        {
            _loudness = FileLoudness.FromFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _loudness = null;
        }

        return _loudness;
    }

    OtoExport MonoExport() => new(
        Math.Round(-_mono.Offset * 1000.0),
        Math.Round(_mono.Consonant * 1000.0),
        _mono.Blank,
        EngineOverlap(_overlap));

    OtoExport RenzokuExport()
    {
        var rest = FindOto(_rest);
        return new(
            Math.Round(-_renzoku.Offset * 1000.0),
            Math.Round(_renzoku.Consonant * 1000.0),
            _renzoku.Blank,
            new Dictionary<string, double>(),
            "renzoku",
            _renzoku.Overlap / 100.0,
            Math.Round(-rest.Offset * 1000.0),
            Math.Round(rest.Consonant * 1000.0),
            rest.Blank,
            EngineOverlap(StoredMap(rest.Overlap)));
    }

    OtoExport CvvcExport()
    {
        var lead = FindOto(_lead);
        return new(
            0,
            Math.Round(_cvvc.Consonant * 1000.0),
            _cvvc.Blank,
            PercentMap(_cvvc.Overlap, DefaultOverlap),
            "cvvc",
            0.5,
            0,
            200,
            0,
            null,
            Math.Round(-_cvvc.VcOffset * 1000.0),
            PercentMap(_cvvc.VcConsonant, DefaultVcConsonant),
            PercentMap(_cvvc.VcBlank, DefaultVcBlank),
            _cvvc.VcOverlap / 100.0,
            Math.Round(-lead.Offset * 1000.0),
            Math.Round(lead.Consonant * 1000.0),
            lead.Blank,
            EngineOverlap(StoredMap(lead.Overlap)));
    }

    static Dictionary<string, double> EngineOverlap(Dictionary<string, double> stored)
    {
        var overlap = new Dictionary<string, double>(ConsonantOverlap.Kinds.Length);
        foreach (var (_, key, _) in ConsonantOverlap.Kinds)
            overlap[key] = ConsonantOverlap.ToEngine(stored.TryGetValue(key, out var value) ? value : DefaultOverlap);
        return overlap;
    }

    static Dictionary<string, double> StoredMap(Dictionary<string, double> source)
    {
        var map = new Dictionary<string, double>(ConsonantOverlap.Kinds.Length);
        foreach (var (_, key, _) in ConsonantOverlap.Kinds)
            map[key] = source.TryGetValue(key, out var value) ? ConsonantOverlap.Stored(value) : DefaultOverlap;
        return map;
    }

    static Dictionary<string, double> PercentMap(Dictionary<string, double> source, double fallback)
    {
        var map = new Dictionary<string, double>(ConsonantOverlap.Kinds.Length);
        foreach (var (_, key, _) in ConsonantOverlap.Kinds)
        {
            var percent = source.TryGetValue(key, out var value) ? value : fallback;
            map[key] = Math.Clamp(percent, 0, 100) / 100.0;
        }

        return map;
    }

    void ApplyMono(OtoPreset preset)
    {
        _mono.Offset = preset.Offset;
        _mono.Consonant = preset.Consonant;
        _mono.Blank = preset.Blank;
        _overlap.Clear();
        foreach (var (_, key, _) in ConsonantOverlap.Kinds)
        {
            _overlap[key] = preset.Overlap.TryGetValue(key, out var value)
                ? ConsonantOverlap.Stored(value)
                : DefaultOverlap;
        }
    }

    void CopyMono(OtoPreset preset)
    {
        preset.Offset = Math.Round(_mono.Offset, 3);
        preset.Consonant = Math.Round(_mono.Consonant, 3);
        preset.Blank = Math.Round(_mono.Blank, 3);
        preset.Overlap.Clear();
        foreach (var (_, key, _) in ConsonantOverlap.Kinds)
        {
            preset.Overlap[key] = ConsonantOverlap.Stored(
                _overlap.TryGetValue(key, out var value) ? value : DefaultOverlap);
        }
    }

    void ApplyCvvc(CvvcPreset preset)
    {
        _cvvc.Consonant = preset.Consonant;
        _cvvc.Blank = preset.Blank;
        _cvvc.VcOffset = preset.VcOffset;
        _cvvc.VcOverlap = CvvcPresetStore.Percent(preset.VcOverlap, DefaultOverlap);
        Replace(_cvvc.Overlap, preset.Overlap);
        Replace(_cvvc.VcConsonant, preset.VcConsonant);
        Replace(_cvvc.VcBlank, preset.VcBlank);
    }

    void CopyCvvc(CvvcPreset preset)
    {
        preset.Consonant = Math.Round(_cvvc.Consonant, 3);
        preset.Blank = Math.Round(_cvvc.Blank, 3);
        preset.VcOffset = Math.Round(_cvvc.VcOffset, 3);
        preset.VcOverlap = _cvvc.VcOverlap;
        preset.Overlap = new Dictionary<string, double>(_cvvc.Overlap);
        preset.VcConsonant = new Dictionary<string, double>(_cvvc.VcConsonant);
        preset.VcBlank = new Dictionary<string, double>(_cvvc.VcBlank);
    }

    string? SaveCvvc(string? raw)
    {
        var error = PresetUi.ReservedNameError(raw ?? "", name => name == CvvcPreset.BuiltInName, "初期値という名前は使えません。");
        if (error != null)
            return error;
        var name = (raw ?? "").Trim();
        var existing = _cvvcPresets.FirstOrDefault(preset => preset.Name == name);
        if (existing == null)
        {
            existing = new CvvcPreset { Name = name };
            _cvvcPresets.Add(existing);
        }

        CopyCvvc(existing);
        _cvvcName = name;
        CvvcPresetStore.Save(_cvvcPresets);
        return null;
    }

    bool MonoDirty()
    {
        var preset = FindOto(_preset);
        if (Math.Round(_mono.Offset, 3) != Math.Round(preset.Offset, 3))
            return true;
        if (Math.Round(_mono.Consonant, 3) != Math.Round(preset.Consonant, 3))
            return true;
        if (Math.Round(_mono.Blank, 3) != Math.Round(preset.Blank, 3))
            return true;
        foreach (var (_, key, _) in ConsonantOverlap.Kinds)
        {
            var expected = preset.Overlap.TryGetValue(key, out var value)
                ? ConsonantOverlap.Stored(value)
                : DefaultOverlap;
            var actual = _overlap.TryGetValue(key, out var intent) ? intent : DefaultOverlap;
            if (Math.Round(actual, 3) != Math.Round(expected, 3))
                return true;
        }

        return false;
    }

    bool CvvcDirty()
    {
        var preset = FindCvvc(_cvvcName);
        if (Math.Round(_cvvc.Consonant, 3) != Math.Round(preset.Consonant, 3))
            return true;
        if (Math.Round(_cvvc.Blank, 3) != Math.Round(preset.Blank, 3))
            return true;
        if (Math.Round(_cvvc.VcOffset, 3) != Math.Round(preset.VcOffset, 3))
            return true;
        if (Math.Round(_cvvc.VcOverlap) != Math.Round(CvvcPresetStore.Percent(preset.VcOverlap, DefaultOverlap)))
            return true;
        return !SamePercent(_cvvc.Overlap, preset.Overlap, DefaultOverlap)
            || !SamePercent(_cvvc.VcConsonant, preset.VcConsonant, DefaultVcConsonant)
            || !SamePercent(_cvvc.VcBlank, preset.VcBlank, DefaultVcBlank);
    }

    static bool SamePercent(Dictionary<string, double> actual, Dictionary<string, double> expected, double fallback)
    {
        foreach (var (_, key, _) in ConsonantOverlap.Kinds)
        {
            var left = actual.TryGetValue(key, out var value) ? value : fallback;
            var right = expected.TryGetValue(key, out var stored)
                ? CvvcPresetStore.Percent(stored, fallback)
                : fallback;
            if (Math.Round(left) != Math.Round(right))
                return false;
        }

        return true;
    }

    OtoPreset FindOto(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == OtoPreset.BuiltInName)
            return BuiltInOto();
        return _presets.FirstOrDefault(preset => preset.Name == name) ?? BuiltInOto();
    }

    CvvcPreset FindCvvc(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == CvvcPreset.BuiltInName)
            return BuiltInCvvc();
        return _cvvcPresets.FirstOrDefault(preset => preset.Name == name) ?? BuiltInCvvc();
    }

    static OtoPreset BuiltInOto() => new()
    {
        Name = OtoPreset.BuiltInName,
        BuiltIn = true,
        Offset = 0,
        Consonant = 0.2,
        Blank = 0,
    };

    static CvvcPreset BuiltInCvvc() => new()
    {
        Name = CvvcPreset.BuiltInName,
        BuiltIn = true,
        Consonant = 0.2,
        Blank = 0,
        VcOffset = 0,
        VcOverlap = DefaultOverlap,
    };

    static bool PutPercent(Dictionary<string, double> dest, string key, double value, double fallback)
    {
        if (!Known(key))
            return false;
        dest[key] = CvvcPresetStore.Percent(value, fallback);
        return true;
    }

    static bool Known(string key)
    {
        foreach (var (_, kind, _) in ConsonantOverlap.Kinds)
        {
            if (kind == key)
                return true;
        }

        return false;
    }

    static Dictionary<string, double> Shown(Dictionary<string, double> source, double fallback)
    {
        var map = new Dictionary<string, double>(ConsonantOverlap.Kinds.Length);
        foreach (var (_, key, _) in ConsonantOverlap.Kinds)
            map[key] = source.TryGetValue(key, out var value) ? value : fallback;
        return map;
    }

    static void Replace(Dictionary<string, double> dest, Dictionary<string, double>? source)
    {
        dest.Clear();
        if (source == null)
            return;
        foreach (var (key, value) in source)
            dest[key] = value;
    }

    static double Seconds(double value, double lo, double hi, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return fallback;
        return Math.Clamp(Math.Round(value, 3), lo, hi);
    }
}
