using Microsoft.UI.Dispatching;

namespace Otoasobi;

public sealed partial class MainPage
{
    readonly Dictionary<string, double[]> _loudnessByFile = new(StringComparer.OrdinalIgnoreCase);
    DispatcherQueueTimer? _otoTableTimer;
    int _loudnessGen;
    string? _revealFile;
    int _revealIndex = -1;
    int _tableFocus = -1;
    bool _revealNav;

    void WireOtoTable()
    {
        OtoParams.RowChosen += (_, row) => RevealOtoRow(row.FileName, row.Index, row.Mode);
        OtoParams.ValuesEdited += (_, edits) => ApplyOtoEdits(edits);
        OtoParams.AliasEdited += (_, edits) => ApplyAliasEdits(edits);
        OtoParams.CommentEdited += (_, edits) => ApplyCommentEdits(edits);
        Editor.OtoFocused += (_, index) =>
        {
            if (_shownFile == null)
                return;
            _tableFocus = index;
            OtoParams.SetEmphasis(_shownFile.Name, index, CurrentMode());
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
        var empty = !_analyzed
            ? "解析すると、oto.ini に出す値がここに出ます。"
            : SelectedAliasModes().Count == 0
                ? "生成するエイリアスが選ばれていません。"
                : "書き出す行がありません。";
        OtoParams.Show(BuildOtoSnapshots(), empty);
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
        var drafts = CollectOtoDrafts();
        if (drafts.Count == 0)
            return rows;
        var aliases = new string[drafts.Count];
        var names = new string?[drafts.Count];
        for (var i = 0; i < drafts.Count; i++)
        {
            aliases[i] = drafts[i].Alias;
            names[i] = drafts[i].Name;
        }

        var numbered = OtoIni.ResolveAliases(aliases, names);
        var duplicates = OtoIni.DuplicateAliasFlags(numbered);
        for (var i = 0; i < drafts.Count; i++)
        {
            var draft = drafts[i];
            var shown = draft.Shown;
            var tuned = draft.Tuned;
            rows.Add(new OtoParamSnapshot
            {
                FileName = draft.FileName,
                FileLabel = Path.GetFileNameWithoutExtension(draft.FileName),
                Mode = draft.Mode,
                Index = draft.Index,
                Alias = draft.Alias,
                AliasText = numbered[i],
                AliasPinned = !string.IsNullOrEmpty(draft.Name),
                AliasDuplicate = duplicates[i],
                Comment = draft.Comment ?? "",
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

        return rows;
    }

    List<OtoTableDraft> CollectOtoDrafts()
    {
        var drafts = new List<OtoTableDraft>();
        if (!_analyzed || _files.Count == 0)
            return drafts;
        foreach (var file in _files)
        {
            if (file.Spans.Count == 0)
                continue;
            foreach (var mode in SelectedAliasModes())
                AppendOtoDrafts(drafts, file, mode);
        }

        return drafts;
    }

    void AppendOtoDrafts(List<OtoTableDraft> drafts, WavFile file, string mode)
    {
        var auto = new List<OtoMark>();
        _loudnessByFile.TryGetValue(file.Name, out var loud);
        OtoMarks.Fill(file, ExportFor(mode), auto, loud);
        var tunes = _adjust.For(mode);
        for (var i = 0; i < auto.Count; i++)
        {
            double?[]? points = null;
            string? name = null;
            string? comment = null;
            foreach (var entry in tunes)
            {
                if (entry.Index != i || !entry.File.Equals(file.Name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(entry.Alias, auto[i].Alias, StringComparison.Ordinal))
                    continue;
                points = entry.Points();
                name = string.IsNullOrEmpty(entry.Name) ? null : entry.Name;
                comment = string.IsNullOrEmpty(entry.Comment) ? null : entry.Comment;
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
            drafts.Add(new OtoTableDraft(file.Name, mode, i, auto[i].Alias, name, comment, shown, tuned, any));
        }
    }

    void ApplyOtoEdits(IReadOnlyList<OtoParamEdit> edits)
    {
        if (_applyingSave || edits.Count == 0)
            return;
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
            var mode = string.IsNullOrEmpty(edit.Mode) ? CurrentMode() : edit.Mode;
            OtoMarks.Fill(file, ExportFor(mode), autoList, loud);
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

            _adjust.Remember(mode, entry);
            changed = true;
            if (_shownFile != null
                && mode == CurrentMode()
                && _shownFile.Name.Equals(file.Name, StringComparison.OrdinalIgnoreCase))
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
            WithSkip("もう一度「oto.iniを生成」を押すと、oto.ini に反映されます。"));
    }

    void ApplyAliasEdits(IReadOnlyList<OtoAliasEdit> edits)
    {
        if (_applyingSave || edits.Count == 0)
            return;
        var touchedShown = false;
        var changed = false;
        foreach (var edit in edits)
        {
            var drafts = CollectOtoDrafts();
            var index = -1;
            for (var i = 0; i < drafts.Count; i++)
            {
                var draft = drafts[i];
                if (!SameOtoRow(draft, edit.FileName, edit.Mode, edit.Index, edit.Alias))
                    continue;
                index = i;
                break;
            }

            if (index < 0)
                continue;
            var aliases = new string[drafts.Count];
            var names = new string?[drafts.Count];
            for (var i = 0; i < drafts.Count; i++)
            {
                aliases[i] = drafts[i].Alias;
                names[i] = drafts[i].Name;
            }

            names[index] = null;
            var natural = OtoIni.ResolveAliases(aliases, names)[index];
            var stored = string.Equals(edit.Name, natural, StringComparison.Ordinal) ? null : edit.Name;
            if (string.Equals(stored ?? "", drafts[index].Name ?? "", StringComparison.Ordinal))
                continue;
            _adjust.RememberName(drafts[index].Mode, drafts[index].FileName, drafts[index].Index, drafts[index].Alias, stored);
            changed = true;
            if (_shownFile != null && _shownFile.Name.Equals(drafts[index].FileName, StringComparison.OrdinalIgnoreCase))
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
            WithSkip("もう一度「oto.iniを生成」を押すと、oto.ini に反映されます。"));
    }

    void ApplyCommentEdits(IReadOnlyList<OtoCommentEdit> edits)
    {
        if (_applyingSave || edits.Count == 0)
            return;
        var changed = false;
        foreach (var edit in edits)
        {
            var drafts = CollectOtoDrafts();
            OtoTableDraft? draft = null;
            foreach (var item in drafts)
            {
                if (!SameOtoRow(item, edit.FileName, edit.Mode, edit.Index, edit.Alias))
                    continue;
                draft = item;
                break;
            }

            if (draft == null)
                continue;
            var stored = string.IsNullOrEmpty(edit.Comment) ? null : edit.Comment;
            if (string.Equals(stored ?? "", draft.Comment ?? "", StringComparison.Ordinal))
                continue;
            _adjust.RememberComment(draft.Mode, draft.FileName, draft.Index, draft.Alias, stored);
            changed = true;
        }

        if (!changed)
            return;
        ScheduleSession();
        RefreshOtoTable();
    }

    void RevealOtoRow(string fileName, int index, string mode)
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
        ShowEstimationMode(mode);
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

    static bool SameOtoRow(OtoTableDraft draft, string fileName, string mode, int index, string alias) =>
        draft.Index == index
        && draft.Mode == mode
        && draft.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(draft.Alias, alias, StringComparison.Ordinal);

    sealed record OtoTableDraft(string FileName, string Mode, int Index, string Alias, string? Name, string? Comment, OtoMark Shown, bool[] Tuned, bool Any);
}
