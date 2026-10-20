using Microsoft.UI.Dispatching;

namespace Otoasobi;

public sealed partial class MainPage
{
    readonly Dictionary<string, double[]> _loudnessByFile = new(StringComparer.OrdinalIgnoreCase);
    DispatcherQueueTimer? _otoTableTimer;
    int _loudnessGen;
    string? _revealFile;
    int _revealIndex = -1;
    bool _revealNav;

    void WireOtoTable()
    {
        OtoParams.RowChosen += (_, row) => RevealOtoRow(row.FileName, row.Index);
        OtoParams.ValuesEdited += (_, edits) => ApplyOtoEdits(edits);
        Editor.OtoFocused += (_, index) =>
        {
            if (_shownFile == null)
                return;
            if (index < 0)
                OtoParams.ClearSelection();
            else
                OtoParams.Select(_shownFile.Name, index);
        };
    }

    void ScheduleOtoTable()
    {
        var queue = DispatcherQueue;
        if (queue == null)
        {
            RefreshOtoTable();
            return;
        }

        if (_otoTableTimer == null)
        {
            _otoTableTimer = queue.CreateTimer();
            _otoTableTimer.IsRepeating = false;
            _otoTableTimer.Interval = TimeSpan.FromMilliseconds(80);
            _otoTableTimer.Tick += (_, _) => RefreshOtoTable();
        }

        _otoTableTimer.Stop();
        _otoTableTimer.Start();
    }

    void RefreshOtoTable()
    {
        _otoTableTimer?.Stop();
        OtoParams.Show(BuildOtoSnapshots(), _analyzed
            ? "書き出す行がありません。"
            : "解析すると、oto.ini に出す値がここに出ます。");
    }

    void ClearLoudness()
    {
        _loudnessGen++;
        _loudnessByFile.Clear();
        _revealFile = null;
    }

    void RememberLoudness(string name, double[]? loudness)
    {
        if (loudness == null || loudness.Length == 0)
            return;
        _loudnessByFile[name] = (double[])loudness.Clone();
    }

    void StartLoudness()
    {
        if (_folder == null || _files.Count == 0)
            return;
        var folder = _folder;
        var names = _files.Select(file => file.Name).ToArray();
        var queue = DispatcherQueue;
        if (queue == null)
            return;
        var gen = ++_loudnessGen;
        _ = Task.Run(() =>
        {
            foreach (var name in names)
            {
                if (gen != _loudnessGen)
                    return;
                double[]? curve;
                try
                {
                    curve = FileLoudness.FromFile(Path.Combine(folder, name));
                }
                catch (Exception)
                {
                    continue;
                }

                if (curve == null || gen != _loudnessGen)
                    continue;
                var captured = curve;
                var capturedName = name;
                queue.TryEnqueue(() =>
                {
                    if (gen != _loudnessGen || _loudnessByFile.ContainsKey(capturedName))
                        return;
                    _loudnessByFile[capturedName] = captured;
                    ScheduleOtoTable();
                });
            }
        });
    }

    List<OtoParamSnapshot> BuildOtoSnapshots()
    {
        var rows = new List<OtoParamSnapshot>();
        if (!_analyzed || _files.Count == 0)
            return rows;
        var export = CurrentExport();
        var tunes = _adjust.For(CurrentMode());
        foreach (var file in _files)
        {
            if (file.Spans.Count == 0)
                continue;
            var auto = new List<OtoMark>();
            _loudnessByFile.TryGetValue(file.Name, out var loud);
            OtoMarks.Fill(file, export, auto, loud);
            for (var i = 0; i < auto.Count; i++)
            {
                double?[]? points = null;
                foreach (var entry in tunes)
                {
                    if (entry.Index != i || !entry.File.Equals(file.Name, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.Equals(entry.Alias, auto[i].Alias, StringComparison.Ordinal))
                        continue;
                    points = entry.Points();
                    break;
                }

                var shown = OtoTune.Apply(auto[i], points, Math.Max(file.Duration, 0));
                var tuned = new bool[OtoTune.PointCount];
                if (points != null)
                {
                    for (var point = 0; point < tuned.Length; point++)
                        tuned[point] = points[point] != null;
                }

                var any = false;
                foreach (var flag in tuned)
                    any |= flag;
                rows.Add(new OtoParamSnapshot
                {
                    FileName = file.Name,
                    FileLabel = Path.GetFileNameWithoutExtension(file.Name),
                    Index = i,
                    Alias = auto[i].Alias,
                    AliasLabel = any ? shown.Alias + " *" : shown.Alias,
                    Offset = OtoIni.Field(shown, 0),
                    Overlap = OtoIni.Field(shown, 1),
                    Preutter = OtoIni.Field(shown, 2),
                    Consonant = OtoIni.Field(shown, 3),
                    Blank = OtoIni.Field(shown, 4),
                    OffsetTuned = tuned[0],
                    OverlapTuned = tuned[1],
                    PreutterTuned = tuned[2],
                    ConsonantTuned = tuned[3],
                    BlankTuned = tuned[4],
                });
            }
        }

        return rows;
    }

    void ApplyOtoEdits(IReadOnlyList<OtoParamEdit> edits)
    {
        if (_applyingSave || edits.Count == 0)
            return;
        var export = CurrentExport();
        var touchedShown = false;
        var changed = false;
        foreach (var edit in edits)
        {
            WavFile? file = null;
            foreach (var candidate in _files)
            {
                if (candidate.Name.Equals(edit.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    file = candidate;
                    break;
                }
            }

            if (file == null)
                continue;
            var autoList = new List<OtoMark>();
            _loudnessByFile.TryGetValue(file.Name, out var loud);
            OtoMarks.Fill(file, export, autoList, loud);
            if (edit.Index < 0 || edit.Index >= autoList.Count)
                continue;
            var auto = autoList[edit.Index];
            if (!string.Equals(auto.Alias, edit.Alias, StringComparison.Ordinal))
                continue;
            var desired = OtoIni.FromFields(auto, edit.Offset, edit.Overlap, edit.Preutter, edit.Consonant, edit.Blank);
            var placed = OtoTune.Place(
                auto,
                desired.Offset,
                desired.Overlap,
                desired.Preutter,
                desired.Consonant,
                desired.Blank,
                [true, true, true, true, true],
                Math.Max(file.Duration, 0));
            var entry = new OtoAdjustedEntry
            {
                File = file.Name,
                Index = edit.Index,
                Alias = auto.Alias,
            };
            for (var point = 0; point < OtoTune.PointCount; point++)
            {
                var at = OtoTune.RoundSeconds(placed.At(point));
                entry.SetPoint(point, Math.Abs(at - auto.At(point)) <= OtoTune.MagnetSeconds ? null : at);
            }

            _adjust.Remember(CurrentMode(), entry);
            changed = true;
            if (_shownFile != null && _shownFile.Name.Equals(file.Name, StringComparison.OrdinalIgnoreCase))
                touchedShown = true;
        }

        if (!changed)
            return;
        if (touchedShown && _shownFile != null)
            Editor.SetTunes(TunesFor(_shownFile));
        ScheduleSession();
        RefreshOtoTable();
        if (!_wrote)
            return;
        _wrote = false;
        ShowNotice(
            "修正はまだファイルに入っていません",
            WithSkip("もう一度「oto.iniを作成」を押すと、oto.ini に反映されます。"));
    }

    void RevealOtoRow(string fileName, int index)
    {
        WavFile? file = null;
        foreach (var candidate in _files)
        {
            if (candidate.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                file = candidate;
                break;
            }
        }

        if (file == null)
            return;
        _revealFile = file.Name;
        _revealIndex = index;
        FileList.ScrollIntoView(file);
        if (!ReferenceEquals(FileList.SelectedItem, file))
        {
            _revealNav = true;
            FileList.SelectedItem = file;
            _revealNav = false;
            return;
        }

        if (ReferenceEquals(_shownFile, file) && Editor.HasContent)
        {
            Editor.FocusOto(index);
            _revealFile = null;
        }
    }

    void TryRevealOto(WavFile file)
    {
        if (_revealFile == null || !_revealFile.Equals(file.Name, StringComparison.OrdinalIgnoreCase))
            return;
        var index = _revealIndex;
        _revealFile = null;
        Editor.FocusOto(index);
    }
}
