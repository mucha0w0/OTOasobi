using Otoasobi;

namespace Otoasobi.Probe;

sealed class ProbeRec
{
    string? _folder;
    string _path = "";
    bool _chosen;
    RecLine[] _lines = [];
    bool _manual = true;
    string _guide = "";

    public void OpenFolder(string folder)
    {
        _folder = folder;
        _chosen = false;
        _path = "";
        _lines = [];
        var saved = RecFolderStore.Load(folder);
        _manual = RecFolderStore.UseManualGuide(saved.GuideManual, saved.Guide);
        _guide = RecFolderStore.IsGuideWav(saved.Guide) ? saved.Guide ?? "" : "";
        if (RecFolderStore.ListIsCleared(saved.List))
        {
            _chosen = true;
            return;
        }

        var path = saved.List;
        if (string.IsNullOrWhiteSpace(path))
        {
            var fallback = Path.Combine(folder, RecList.DefaultFileName);
            path = File.Exists(fallback) ? fallback : null;
        }

        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            Load(path, remember: false);
    }

    public string? Load(string? path, bool remember)
    {
        if (string.IsNullOrEmpty(_folder))
            return "フォルダを開いてください。";
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return "録音リストを読めませんでした。";
        try
        {
            _lines = RecList.Parse(File.ReadAllBytes(path)).ToArray();
            _path = path;
            _chosen = true;
            if (remember)
                RecListHistory.Remember(path);
            Store(_path);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    public void ClearChoice()
    {
        _path = "";
        _lines = [];
        _chosen = true;
        if (!string.IsNullOrEmpty(_folder))
            Store("");
    }

    public bool Manual => _manual;

    public string GuidePath => _guide;

    public string? SetManual(bool manual)
    {
        _manual = manual;
        SaveGuide();
        return null;
    }

    public string? UseGuide(string? path)
    {
        if (string.IsNullOrEmpty(_folder))
            return "フォルダを開いてください。";
        if (!RecFolderStore.IsGuideWav(path) || path == null)
            return "ガイドは WAV を選んでください。同じ名前の txt が対になります。";
        string? directory = null;
        try
        {
            directory = Path.GetDirectoryName(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "ガイドは WAV を選んでください。同じ名前の txt が対になります。";
        }

        if (directory != null && string.Equals(Path.GetFullPath(directory), Path.GetFullPath(_folder), StringComparison.OrdinalIgnoreCase))
            return "録音先のフォルダにあるファイルは選べません。";
        if (!File.Exists(path))
            return "ガイドを読めませんでした。";
        _guide = path;
        _manual = false;
        RecGuideHistory.Remember(path);
        SaveGuide();
        if (!File.Exists(RecFolderStore.GuideTextPath(path)))
            return "missing-text:" + Path.GetFileName(RecFolderStore.GuideTextPath(path));
        return null;
    }

    public void ClearGuide()
    {
        _guide = "";
        SaveGuide();
    }

    public object GuideSnapshot()
    {
        var recent = RecGuideHistory.Load();
        return new
        {
            type = "guideFile",
            manual = _manual,
            path = _guide,
            label = string.IsNullOrEmpty(_guide) ? "" : Path.GetFileName(_guide),
            recent = recent.Select(path => new { path, label = Path.GetFileName(path) }).ToArray(),
        };
    }

    void SaveGuide()
    {
        if (string.IsNullOrEmpty(_folder))
            return;
        try
        {
            var file = RecFolderStore.Load(_folder);
            file.Guide = string.IsNullOrEmpty(_guide) ? null : _guide;
            file.GuideManual = _manual;
            RecFolderStore.Save(_folder, file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public object Snapshot(IReadOnlyList<string> wavNames)
    {
        var recent = RecListHistory.Load();
        var lines = _lines.Select(line =>
        {
            var wav = RecNames.WavName(line.Sing);
            var recorded = line.NameError == null
                && !string.IsNullOrEmpty(_folder)
                && File.Exists(Path.Combine(_folder, wav));
            return new
            {
                sing = line.Sing,
                comment = line.Comment,
                error = line.NameError,
                recorded,
                wav,
            };
        }).ToArray();
        return new
        {
            type = "recList",
            path = _chosen ? _path : "",
            recent = recent.Select(path => new { path, label = RecListHistory.Caption(path, recent) }).ToArray(),
            lines,
            outside = RecNames.Outside(wavNames, _lines).ToArray(),
        };
    }

    void Store(string list)
    {
        if (string.IsNullOrEmpty(_folder))
            return;
        try
        {
            var file = RecFolderStore.Load(_folder);
            file.List = list;
            RecFolderStore.Save(_folder, file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
