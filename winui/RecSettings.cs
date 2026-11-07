namespace Otoasobi;

// マイク、再生先、ガイド音はマシンの設定。音源フォルダには置かない。
sealed class RecAppSettings
{
    public static event EventHandler? DevicesChanged;

    public static event EventHandler<string?>? CaptureStateChanged;

    public static string? CaptureMessage { get; private set; }

    public string Note { get; set; } = "C4";

    public double GuideVolume { get; set; } = 0.7;

    // 1 が元の速さ。ファイルのガイドだけに効く。
    public double GuideSpeed { get; set; } = 1;

    // 半音。正が高い。ファイルのガイドだけに効く。
    public int GuidePitch { get; set; }

    public bool CountIn { get; set; }

    public int Bpm { get; set; } = 120;

    public bool GuideWhileRecording { get; set; }

    public bool AutoAdvance { get; set; }

    public string? DeviceId { get; set; }

    public string? OutputDeviceId { get; set; }

    public static string FilePath { get; } = Path.Combine(AppPaths.DataRoot, "recording.json");

    public static RecAppSettings Load()
    {
        var settings = JsonFiles.Read<RecAppSettings>(FilePath) ?? new RecAppSettings();
        if (!RecTone.Notes.Contains(settings.Note))
            settings.Note = "C4";
        settings.GuideVolume = Math.Clamp(settings.GuideVolume, 0, 1);
        settings.GuideSpeed = Math.Clamp(settings.GuideSpeed, 0.5, 2);
        settings.GuidePitch = Math.Clamp(settings.GuidePitch, -12, 12);
        settings.Bpm = Math.Clamp(settings.Bpm, 40, 240);
        settings.DeviceId = BlankToNull(settings.DeviceId);
        settings.OutputDeviceId = BlankToNull(settings.OutputDeviceId);
        return settings;
    }

    public static void SaveDevices(string? inputId, string? outputId)
    {
        var settings = Load();
        settings.DeviceId = BlankToNull(inputId);
        settings.OutputDeviceId = BlankToNull(outputId);
        settings.Save();
        DevicesChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void ReportCapture(string? message)
    {
        CaptureMessage = string.IsNullOrWhiteSpace(message) ? null : message;
        CaptureStateChanged?.Invoke(null, CaptureMessage);
    }

    static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    public void Save()
    {
        try
        {
            JsonFiles.Write(FilePath, this);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
