using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Otoasobi;

public sealed partial class MainPage
{
    RadioButton? _frqPreutter;

    void FrqPreutter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radio)
            return;
        if (ReferenceEquals(_frqPreutter, radio))
        {
            _frqPreutter = null;
            radio.IsChecked = false;
            return;
        }

        _frqPreutter = radio;
    }

    void WriteFrq_Click(object sender, RoutedEventArgs e)
    {
        _ = WriteFrqAsync();
    }

    async Task WriteFrqAsync()
    {
        if (_busy || _folder == null || _files.Count == 0)
            return;
        var folder = _folder;
        var mode = "all";
        string? only = null;
        if (FrqModeMissing.IsChecked == true)
            mode = "missing";
        else if (FrqModeSelected.IsChecked == true)
        {
            mode = "selected";
            if (FileList.SelectedItem is not WavFile selected)
            {
                ShowNotice(".frqを生成できませんでした", "ファイルを選んでください。");
                return;
            }

            only = selected.Name;
        }

        object? clear = null;
        if (FrqClearPreutter.IsChecked == true || FrqNonePreutter.IsChecked == true)
            clear = PreutterClears();

        _busy = true;
        UpdateCommands();
        Editor.IsEnabled = false;
        ConfigBody.IsEnabled = false;
        ShowStatus(".frqを計算しています");
        var queue = DispatcherQueue;
        try
        {
            _analysis ??= new AnalysisClient();
            var result = await _analysis.WriteFrqAsync(folder, mode, only, clear, (done, total) =>
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
                ShowFrqFor(FileList.SelectedItem as WavFile, reload: true);
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

    object[] PreutterClears()
    {
        var map = new Dictionary<string, List<double[]>>(StringComparer.OrdinalIgnoreCase);
        foreach (var draft in CollectOtoDrafts())
        {
            if (!map.TryGetValue(draft.FileName, out var ranges))
            {
                ranges = [];
                map[draft.FileName] = ranges;
            }

            var start = draft.Shown.Offset;
            var end = draft.Shown.Preutter;
            if (double.IsNaN(start) || double.IsNaN(end) || end <= start)
                continue;
            ranges.Add([start, end]);
        }

        return map.Select(pair => new { name = pair.Key, ranges = pair.Value }).ToArray();
    }
}
