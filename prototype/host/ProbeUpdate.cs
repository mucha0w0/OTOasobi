using Otoasobi;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace Otoasobi.Probe;

sealed class ProbeUpdate
{
    int _running;
    UpdateInfo? _pending;
    EngineRelease? _release;
    EngineParts _need;

    public async Task CheckAsync(Action<string, string, string> post)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return;
        try
        {
            UpdateManager manager;
            try
            {
                manager = Create();
            }
            catch (Exception ex)
            {
                post("failed", "更新できませんでした", Message(ex));
                return;
            }

            var installed = manager.IsInstalled;
            UpdateInfo? info = null;
            string? appError = null;
            if (installed)
            {
                try
                {
                    info = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
                }
                catch (NotInstalledException)
                {
                    installed = false;
                }
                catch (Exception)
                {
                    appError = "更新情報を取得できませんでした。";
                }
            }

            EngineRelease? release = null;
            string? engineError = null;
            try
            {
                release = await EngineRelease.LoadAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                engineError = ex.Message;
            }

            var state = JsonFiles.Read<EngineState>(AppPaths.EngineStateFile) ?? new EngineState();
            var pythonExists = File.Exists(AppPaths.EnginePython);
            var modelExists = File.Exists(AppPaths.ModelFile);
            var need = release == null
                ? new EngineParts(false, false)
                : EngineNeed.Missing(release.Manifest, state, pythonExists, modelExists);
            if (info == null && !need.Any)
            {
                var problem = appError ?? engineError;
                if (problem == null && !installed)
                {
                    post("current", "更新", "高精度エンジンは入っています。アプリの更新は、Setup からインストールしたときに確認します。");
                    return;
                }

                var current = installed ? manager.CurrentVersion?.ToString() : null;
                var body = problem ?? (current == null ? "いまのバージョンが最新です。" : $"{current} は最新です。");
                post("current", "更新", body);
                return;
            }

            var prompt = UpdatePromptBuilder.Build(
                info?.TargetFullRelease.Version.ToString(),
                info == null ? 0 : Size(info),
                need.Python,
                release?.PythonBytes ?? 0,
                need.Model,
                release?.ModelBytes ?? 0,
                !pythonExists);
            _pending = info;
            _release = release;
            _need = need;
            post("prompt", prompt.Title, prompt.Body);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    public async Task ApplyAsync(Action<string, string, string> post, Action stop)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return;
        try
        {
            var info = _pending;
            var release = _release;
            var need = _need;
            if (need.Python || need.Model || info != null)
                stop();
            if (need.Python && release != null)
            {
                await EngineInstaller.InstallZipAsync(
                    release.PythonLocation,
                    release.Manifest.PythonSha256,
                    AppPaths.EngineDirectory,
                    release.PythonBytes,
                    (done, total) => post("progress", "更新", $"高精度エンジンをダウンロードしています。{done}/{Math.Max(total, 1)}"),
                    () => post("progress", "更新", "高精度エンジンを展開しています。"),
                    CancellationToken.None).ConfigureAwait(false);
                SaveHash(state => state.PythonSha256 = release.Manifest.PythonSha256);
            }

            if (need.Model && release != null)
            {
                await EngineInstaller.InstallFileAsync(
                    release.ModelLocation,
                    release.Manifest.ModelSha256,
                    AppPaths.ModelFile,
                    release.ModelBytes,
                    (done, total) => post("progress", "更新", $"音声モデルをダウンロードしています。{done}/{Math.Max(total, 1)}"),
                    CancellationToken.None).ConfigureAwait(false);
                SaveHash(state => state.ModelSha256 = release.Manifest.ModelSha256);
            }

            if (info != null)
            {
                var manager = Create();
                post("progress", "更新", "アプリをダウンロードしています。");
                await manager.DownloadUpdatesAsync(info, percent => post("progress", "更新", $"アプリをダウンロードしています。{percent}%")).ConfigureAwait(false);
                stop();
                manager.ApplyUpdatesAndRestart(info);
                return;
            }

            if (need.Any)
                post("done", "更新", "音声解析エンジンを更新しました。");
        }
        catch (Exception ex)
        {
            post("failed", "更新できませんでした", Message(ex));
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    public Task InstallFromFolderAsync(string folder, Action<string, string, string> post, Action stop) =>
        InstallPreparedAsync(() => EngineRelease.ReadFolder(folder), null, post, stop);

    public Task InstallFromPackageAsync(string packagePath, Action<string, string, string> post, Action stop)
    {
        string? temp = null;
        return InstallPreparedAsync(
            () =>
            {
                temp = EnginePack.Materialize(packagePath);
                return EngineRelease.ReadFolder(temp);
            },
            () =>
            {
                if (temp != null && Directory.Exists(temp))
                    Directory.Delete(temp, true);
            },
            post,
            stop);
    }

    async Task InstallPreparedAsync(Func<EngineRelease> open, Action? cleanup, Action<string, string, string> post, Action stop)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return;
        try
        {
            var release = open();
            stop();
            await EngineInstaller.InstallZipAsync(
                release.PythonLocation,
                release.Manifest.PythonSha256,
                AppPaths.EngineDirectory,
                release.PythonBytes,
                (done, total) => post("progress", "更新", $"高精度エンジンを読み込んでいます。{done}/{Math.Max(total, 1)}"),
                () => post("progress", "更新", "高精度エンジンを展開しています。"),
                CancellationToken.None).ConfigureAwait(false);
            SaveHash(state => state.PythonSha256 = release.Manifest.PythonSha256);
            await EngineInstaller.InstallFileAsync(
                release.ModelLocation,
                release.Manifest.ModelSha256,
                AppPaths.ModelFile,
                release.ModelBytes,
                (done, total) => post("progress", "更新", $"音声モデルを読み込んでいます。{done}/{Math.Max(total, 1)}"),
                CancellationToken.None).ConfigureAwait(false);
            SaveHash(state => state.ModelSha256 = release.Manifest.ModelSha256);
            post("done", "更新", "音声解析エンジンを更新しました。");
        }
        catch (Exception ex)
        {
            post("failed", "更新できませんでした", Message(ex));
        }
        finally
        {
            try
            {
                cleanup?.Invoke();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            Interlocked.Exchange(ref _running, 0);
        }
    }

    static void SaveHash(Action<EngineState> edit)
    {
        var state = JsonFiles.Read<EngineState>(AppPaths.EngineStateFile) ?? new EngineState();
        edit(state);
        JsonFiles.Write(AppPaths.EngineStateFile, state);
    }

    static long Size(UpdateInfo info)
    {
        var full = info.TargetFullRelease.Size;
        var deltas = info.DeltasToTarget;
        if (deltas == null || deltas.Length == 0)
            return full;
        long sum = 0;
        foreach (var delta in deltas)
            sum += delta.Size;
        if (full > 0 && (sum <= 0 || sum > full))
            return full;
        return sum;
    }

    static UpdateManager Create()
    {
        if (AppPaths.FeedDirectory is string feed)
            return new UpdateManager(feed);
        return new UpdateManager(new GithubSource(AppPaths.GitHubRepoUrl, string.Empty, false));
    }

    static string Message(Exception ex) =>
        string.IsNullOrWhiteSpace(ex.Message) ? "更新を確認できませんでした。" : ex.Message;
}
