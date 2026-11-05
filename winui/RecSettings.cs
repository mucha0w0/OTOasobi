namespace Otoasobi;

// マイクとガイド音はマシンの設定。音源フォルダには置かない。
sealed class RecAppSettings
{
    public string Note { get; set; } = "C4";

    public double GuideVolume { get; set; } = 0.7;

    public bool CountIn { get; set; }

    public int Bpm { get; set; } = 120;

    public bool GuideWhileRecording { get; set; }

    public bool AutoAdvance { get; set; }

    public string? DeviceId { get; set; }

    public static string FilePath { get; } = Path.Combine(AppPaths.DataRoot, "recording.json");

    public static RecAppSettings Load()
    {
        var settings = JsonFiles.Read<RecAppSettings>(FilePath) ?? new RecAppSettings();
        if (!RecTone.Notes.Contains(settings.Note))
            settings.Note = "C4";
        settings.GuideVolume = Math.Clamp(settings.GuideVolume, 0, 1);
        settings.Bpm = Math.Clamp(settings.Bpm, 40, 240);
        settings.DeviceId = string.IsNullOrWhiteSpace(settings.DeviceId) ? null : settings.DeviceId;
        return settings;
    }

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
