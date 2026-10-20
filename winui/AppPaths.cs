namespace Otoasobi;

static class AppPaths
{
    public const string GitHubRepoUrl = "https://github.com/mucha0w0/OTOasobi";
    public const string PackId = "Otoasobi";
    public const string ManifestName = "engine.json";

    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        PackId);

    public static string EngineDirectory => Path.Combine(DataRoot, "engine");

    public static string EnginePython => Path.Combine(EngineDirectory, "python.exe");

    public static string ModelsRoot => Path.Combine(DataRoot, "models");

    // torchaudio が TORCH_HOME/hub/checkpoints/model.pt を探す。
    public static string ModelFile => Path.Combine(ModelsRoot, "hub", "checkpoints", "model.pt");

    public static string EngineStateFile => Path.Combine(DataRoot, "engine-state.json");

    public static string? FeedDirectory
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("OTOASOBI_UPDATE_DIR");
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    public static string LatestReleaseApi
    {
        get
        {
            var parts = new Uri(GitHubRepoUrl).AbsolutePath.Trim('/').Split('/');
            return $"https://api.github.com/repos/{parts[0]}/{parts[1]}/releases/latest";
        }
    }
}
