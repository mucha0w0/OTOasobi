using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Otoasobi;

public sealed partial class MainPage
{
    void WriteFrq_Click(object sender, RoutedEventArgs e)
    {
        _ = WriteFrqAsync();
    }

    async Task WriteFrqAsync()
    {
        if (_busy || _folder == null || _files.Count == 0)
            return;
        var folder = _folder;
        if (CountFrq(folder).Frq > 0)
        {
            var dialog = new ContentDialog
            {
                Title = "すでにある .frq を上書きしますか？",
                Content = "フォルダ直下の WAV と同じ名前の .frq を作り直します。",
                PrimaryButtonText = "上書きする",
                CloseButtonText = "キャンセル",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || _busy || _folder != folder)
                return;
        }

        _busy = true;
        UpdateCommands();
        Editor.IsEnabled = false;
        ConfigBody.IsEnabled = false;
        ShowStatus(".frqを計算しています");
        var queue = DispatcherQueue;
        try
        {
            _analysis ??= new AnalysisClient();
            var result = await _analysis.WriteFrqAsync(folder, (done, total) =>
            {
                queue.TryEnqueue(() =>
                {
                    if (_busy && _folder == folder)
                        ShowFrqProgress(done, total);
                });
            }, (name, done, label) =>
            {
                queue.TryEnqueue(() =>
                {
                    if (_busy && _folder == folder)
                        UpdateFileStatus(name, done, label);
                });
            });
            if (_folder != folder)
                return;
            if (!result.Ok)
            {
                ShowNotice(
                    ".frqを生成できませんでした",
                    string.IsNullOrEmpty(result.Error) ? null : result.Error);
                return;
            }

            ShowNotice($"{result.Written} 件の .frq を書きました", result.Notice);
        }
        catch (Exception ex)
        {
            if (_folder == folder)
                ShowNotice(".frqを生成できませんでした", ex.Message);
        }
        finally
        {
            _busy = false;
            if (_folder == folder)
            {
                Editor.IsEnabled = true;
                ConfigBody.IsEnabled = true;
                UpdateCommands();
                HideStatus();
                RefreshFrqSummary();
            }
        }
    }

    void ShowFrqProgress(int done, int total)
    {
        StatusText.Text = total <= 0 ? ".frqを計算しています" : $"{done} / {total} ファイル";
        StatusProgress.IsIndeterminate = false;
        StatusProgress.Maximum = Math.Max(total, 1);
        StatusProgress.Value = done;
        StatusBar.Visibility = Visibility.Visible;
    }

    void RefreshFrqSummary()
    {
        if (string.IsNullOrEmpty(_folder) || !Directory.Exists(_folder))
        {
            FrqSummary.Text = "フォルダを開くと、WAV と .frq の件数がここに出ます。";
            return;
        }

        var (wavs, frq) = CountFrq(_folder);
        FrqSummary.Text = wavs == 0
            ? "フォルダ直下に WAV がありません。"
            : $"WAV {wavs} 件のうち、.frq は {frq} 件あります。";
    }

    static (int Wavs, int Frq) CountFrq(string folder)
    {
        var wavs = 0;
        var frq = 0;
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                continue;
            wavs++;
            if (File.Exists(Path.ChangeExtension(path, ".frq")))
                frq++;
        }

        return (wavs, frq);
    }
}
