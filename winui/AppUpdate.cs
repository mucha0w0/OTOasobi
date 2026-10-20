using System.Net.Http;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace Otoasobi;

static class AppUpdate
{
    static int _running;

    public static async Task OfferAsync(XamlRoot? root, bool manual)
    {
        if (root == null)
            return;
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return;
        try
        {
            await OfferCore(root, manual);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (manual)
                await ShowSafe(root, "更新できませんでした", MessageOf(ex));
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    static async Task OfferCore(XamlRoot root, bool manual)
    {
        using var cancel = new CancellationTokenSource();
        var window = App.MainWindow;
        void OnClosed(object sender, WindowEventArgs args) => cancel.Cancel();
        if (window != null)
            window.Closed += OnClosed;
        try
        {
            UpdateManager manager;
            try
            {
                manager = CreateManager();
            }
            catch (Exception ex)
            {
                if (manual)
                    await ShowMessage(root, "更新できませんでした", MessageOf(ex));
                return;
            }

            if (!manager.IsInstalled)
            {
                if (manual)
                    await ShowMessage(root, "更新", "Setup からインストールしたときに、更新を確認します。");
                return;
            }

                UpdateInfo? appUpdate = null;
                string? appError = null;
                try
                {
                    appUpdate = await manager.CheckForUpdatesAsync();
                }
                catch (NotInstalledException)
                {
                    if (manual)
                        await ShowMessage(root, "更新", "Setup からインストールしたときに、更新を確認します。");
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    appError = "更新情報を取得できませんでした。";
                }

                EngineRelease? release = null;
                string? engineError = null;
                try
                {
                    release = await EngineRelease.LoadAsync(cancel.Token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    engineError = MessageOf(ex);
                }

                var state = JsonFiles.Read<EngineState>(AppPaths.EngineStateFile);
                var pythonExists = File.Exists(AppPaths.EnginePython);
                var modelExists = File.Exists(AppPaths.ModelFile);
                var need = release == null
                    ? new EngineParts(false, false)
                    : EngineNeed.Missing(release.Manifest, state, pythonExists, modelExists);
                if (appUpdate == null && !need.Any)
                {
                    if (manual)
                    {
                        var problem = Join(appError, engineError);
                        var current = manager.CurrentVersion?.ToString();
                        var latest = string.IsNullOrEmpty(current)
                            ? "いまのバージョンが最新です。"
                            : $"{current} は最新です。";
                        await ShowMessage(root, "更新", problem ?? latest);
                    }

                    return;
                }

                var appVersion = appUpdate == null ? null : appUpdate.TargetFullRelease.Version.ToString();
                var appBytes = appUpdate == null ? 0 : DownloadBytes(appUpdate);
                var pythonBytes = release?.PythonBytes ?? 0;
                var modelBytes = release?.ModelBytes ?? 0;
                var firstEngine = !pythonExists;
                var prompt = UpdatePromptBuilder.Build(
                    appVersion,
                    appBytes,
                    need.Python,
                    pythonBytes,
                    need.Model,
                    modelBytes,
                    firstEngine);
                if (!await Confirm(root, prompt))
                    return;

                var progressTitle = firstEngine && appUpdate == null ? "準備しています" : "更新しています";
                var progress = new UpdateProgressDialog(root, progressTitle, cancel);
                try
                {
                    var tracker = new ProgressTotal(StepBytes(appUpdate, appBytes, need, pythonBytes, modelBytes));
                    if (need.Python || need.Model)
                        MainPage.ShutdownAnalysis();
                    if (need.Python && release != null)
                    {
                        await EngineInstaller.InstallZipAsync(
                            release.PythonLocation,
                            release.Manifest.PythonSha256,
                            AppPaths.EngineDirectory,
                            pythonBytes,
                            (done, _) => tracker.Report(progress, "高精度エンジンをダウンロードしています", done, pythonBytes),
                            () => tracker.Report(progress, "高精度エンジンを展開しています", pythonBytes, pythonBytes),
                            cancel.Token);
                        SavePythonHash(release.Manifest.PythonSha256);
                        tracker.Complete(pythonBytes);
                    }

                    if (need.Model && release != null)
                    {
                        await EngineInstaller.InstallFileAsync(
                            release.ModelLocation,
                            release.Manifest.ModelSha256,
                            AppPaths.ModelFile,
                            modelBytes,
                            (done, _) => tracker.Report(progress, "音声モデルをダウンロードしています", done, modelBytes),
                            cancel.Token);
                        SaveModelHash(release.Manifest.ModelSha256);
                        tracker.Complete(modelBytes);
                    }

                    if (appUpdate != null)
                    {
                        await manager.DownloadUpdatesAsync(
                            appUpdate,
                            percent =>
                            {
                                var current = appBytes * Math.Clamp(percent, 0, 100) / 100;
                                tracker.Report(progress, "アプリをダウンロードしています", current, appBytes);
                            },
                            cancel.Token);
                        MainPage.ShutdownAnalysis();
                        await progress.CloseAsync();
                        manager.ApplyUpdatesAndRestart(appUpdate);
                        return;
                    }
                }
                finally
                {
                    await progress.CloseAsync();
                }

                if (need.Any)
                {
                    var title = firstEngine ? "準備しました" : "更新しました";
                    var body = firstEngine
                        ? "高精度エンジンを準備しました。"
                        : "高精度エンジンを更新しました。";
                    await ShowMessage(root, title, body);
                }
        }
        finally
        {
            if (window != null)
                window.Closed -= OnClosed;
        }
    }

    static UpdateManager CreateManager()
    {
        var feed = AppPaths.FeedDirectory;
        if (feed != null)
            return new UpdateManager(feed);
        return new UpdateManager(new GithubSource(AppPaths.GitHubRepoUrl, null, false));
    }

    static long DownloadBytes(UpdateInfo info)
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

    static long StepBytes(UpdateInfo? appUpdate, long appBytes, EngineParts need, long pythonBytes, long modelBytes)
    {
        long total = 0;
        if (appUpdate != null)
            total += Math.Max(appBytes, 1);
        if (need.Python)
            total += Math.Max(pythonBytes, 1);
        if (need.Model)
            total += Math.Max(modelBytes, 1);
        return Math.Max(total, 1);
    }

    static void SavePythonHash(string hash)
    {
        var state = JsonFiles.Read<EngineState>(AppPaths.EngineStateFile) ?? new EngineState();
        state.PythonSha256 = hash.Trim();
        JsonFiles.Write(AppPaths.EngineStateFile, state);
    }

    static void SaveModelHash(string hash)
    {
        var state = JsonFiles.Read<EngineState>(AppPaths.EngineStateFile) ?? new EngineState();
        state.ModelSha256 = hash.Trim();
        JsonFiles.Write(AppPaths.EngineStateFile, state);
    }

    static string? Join(string? left, string? right)
    {
        if (left == null)
            return right;
        if (right == null)
            return left;
        return left + Environment.NewLine + right;
    }

    static string MessageOf(Exception ex)
    {
        if (ex is OperationCanceledException)
            return "更新を中止しました。";
        if (ex is AcquireLockFailedException)
            return "別の更新が進んでいます。";
        if (ex is HttpRequestException)
            return "更新情報を取得できませんでした。";
        if (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
            return string.IsNullOrWhiteSpace(ex.Message) ? "更新できませんでした。" : ex.Message;
        return "更新できませんでした。";
    }

    static async Task<bool> Confirm(XamlRoot root, UpdatePrompt prompt)
    {
        var dialog = new ContentDialog
        {
            Title = prompt.Title,
            Content = Body(prompt.Body),
            PrimaryButtonText = prompt.Confirm,
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    static async Task ShowMessage(XamlRoot root, string title, string body)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = Body(body),
            CloseButtonText = "閉じる",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root,
        };
        await dialog.ShowAsync();
    }

    static async Task ShowSafe(XamlRoot root, string title, string body)
    {
        try
        {
            await ShowMessage(root, title, body);
        }
        catch (Exception)
        {
        }
    }

    static TextBlock Body(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 420,
    };

    sealed class ProgressTotal
    {
        readonly long _total;
        long _done;

        public ProgressTotal(long total) => _total = Math.Max(1, total);

        public void Report(UpdateProgressDialog dialog, string label, long current, long size)
        {
            var span = Math.Max(size, 1);
            var overall = (_done + Math.Clamp(current, 0, span)) / (double)_total;
            dialog.Report(label, overall);
        }

        public void Complete(long size) => _done += Math.Max(size, 1);
    }
}

sealed class UpdateProgressDialog
{
    readonly ContentDialog _dialog;
    readonly TextBlock _status;
    readonly ProgressBar _bar;
    readonly Task _shown;
    bool _closed;

    public UpdateProgressDialog(XamlRoot root, string title, CancellationTokenSource cancel)
    {
        _status = new TextBlock
        {
            Text = "準備しています",
            TextWrapping = TextWrapping.Wrap,
        };
        _bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
        };
        var panel = new StackPanel
        {
            Spacing = 12,
            MinWidth = 360,
        };
        panel.Children.Add(_status);
        panel.Children.Add(_bar);
        _dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            CloseButtonText = "キャンセル",
            XamlRoot = root,
        };
        _dialog.Closing += (_, _) =>
        {
            if (!_closed)
                cancel.Cancel();
        };
        _shown = _dialog.ShowAsync().AsTask();
    }

    public void Report(string text, double fraction)
    {
        void Apply()
        {
            _status.Text = text;
            _bar.IsIndeterminate = false;
            _bar.Value = Math.Clamp(fraction, 0, 1) * 100;
        }

        if (_dialog.DispatcherQueue.HasThreadAccess)
            Apply();
        else
            _dialog.DispatcherQueue.TryEnqueue(Apply);
    }

    public async Task CloseAsync()
    {
        if (_closed)
            return;
        _closed = true;
        _dialog.Hide();
        try
        {
            await _shown;
        }
        catch (Exception)
        {
        }
    }
}
