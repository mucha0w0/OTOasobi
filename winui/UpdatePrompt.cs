using System.Globalization;

namespace Otoasobi;

sealed class EngineManifest
{
    public string Version { get; set; } = "";

    public string Python { get; set; } = "";

    public string PythonSha256 { get; set; } = "";

    public long PythonSize { get; set; }

    public string Model { get; set; } = "";

    public string ModelSha256 { get; set; } = "";

    public long ModelSize { get; set; }

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Python)
            || string.IsNullOrWhiteSpace(PythonSha256)
            || string.IsNullOrWhiteSpace(Model)
            || string.IsNullOrWhiteSpace(ModelSha256))
            throw new InvalidDataException("engine.json の検証情報がありません。");
    }
}

sealed class EngineState
{
    public string PythonSha256 { get; set; } = "";

    public string ModelSha256 { get; set; } = "";
}

readonly record struct EngineParts(bool Python, bool Model)
{
    public bool Any => Python || Model;
}

static class EngineNeed
{
    public static EngineParts Missing(EngineManifest remote, EngineState? local, bool pythonExists, bool modelExists)
    {
        return new EngineParts(
            !pythonExists || !Same(local?.PythonSha256, remote.PythonSha256),
            !modelExists || !Same(local?.ModelSha256, remote.ModelSha256));
    }

    static bool Same(string? local, string? remote) =>
        !string.IsNullOrWhiteSpace(remote)
        && string.Equals(local?.Trim(), remote.Trim(), StringComparison.OrdinalIgnoreCase);
}

static class ByteSize
{
    public static string Format(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        double value = bytes;
        string[] units = ["KB", "MB", "GB"];
        var unit = -1;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.#} {1}", value, units[unit]);
    }
}

sealed record UpdatePrompt(string Title, string Body, string Confirm);

static class UpdatePromptBuilder
{
    public static UpdatePrompt Build(
        string? appVersion,
        long appBytes,
        bool needPython,
        long pythonBytes,
        bool needModel,
        long modelBytes,
        bool firstEngine)
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(appVersion))
            lines.Add($"アプリ（{ByteSize.Format(appBytes)}）");
        if (needPython)
            lines.Add($"高精度エンジン（{ByteSize.Format(pythonBytes)}）");
        if (needModel)
            lines.Add($"音声モデル（{ByteSize.Format(modelBytes)}）");

        string title;
        string intro;
        string confirm;
        if (!string.IsNullOrEmpty(appVersion))
        {
            title = $"バージョン {appVersion} があります";
            intro = firstEngine
                ? "更新しますか。キャンセルしたときは、音響エンジンで動きます。"
                : "更新しますか。";
            confirm = "更新する";
        }
        else if (firstEngine)
        {
            title = "高精度エンジンを準備します";
            intro = "高精度の解析に使うファイルをダウンロードします。キャンセルしたときは、音響エンジンで動きます。";
            confirm = "準備する";
        }
        else
        {
            title = "高精度エンジンを更新します";
            intro = "更新しますか。";
            confirm = "更新する";
        }

        var body = lines.Count == 0
            ? intro
            : intro + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines);
        return new UpdatePrompt(title, body, confirm);
    }
}
