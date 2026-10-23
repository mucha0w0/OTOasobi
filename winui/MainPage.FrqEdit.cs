using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Otoasobi;

public sealed partial class MainPage
{
    string? _frqPath;

    void FrqTool_Checked(object sender, RoutedEventArgs e)
    {
        if (FrqRoll == null || FrqOctaveBar == null || FrqToolSelect == null)
            return;
        var select = FrqToolSelect.IsChecked == true;
        FrqRoll.Tool = select ? FrqTool.Select : FrqTool.Pen;
        FrqOctaveBar.Visibility = select ? Visibility.Visible : Visibility.Collapsed;
    }

    void FrqOctaveUp_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        FrqRoll.ShiftSelection(1);
    }

    void FrqOctaveDown_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        FrqRoll.ShiftSelection(-1);
    }

    void FrqUnvoice_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        FrqRoll.ClearSelectedPitch();
    }

    void FrqRoll_Edited(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_frqPath) || FrqRoll.Document is not FrqFile document)
            return;
        try
        {
            document.Write(_frqPath);
            FrqRoll.MarkClean();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ShowNotice(".frqを保存できませんでした", ex.Message);
        }
    }

    void UpdateFrqSelection()
    {
        if (FrqOctaveUpButton == null)
            return;
        var on = !_busy && FrqRoll.SelectionCount > 0 && FrqRoll.HasDocument;
        FrqOctaveUpButton.IsEnabled = on;
        FrqOctaveDownButton.IsEnabled = on;
        FrqUnvoiceButton.IsEnabled = on;
    }

    void FlushFrq() => FrqRoll.Flush();

    void ShowFrqFor(WavFile? file, bool reload = false)
    {
        if (reload)
            FrqRoll.Cancel();
        else
            FrqRoll.Flush();

        if (file == null || string.IsNullOrEmpty(_folder))
        {
            _frqPath = null;
            FrqRoll.SetDocument(null, 0, "ファイルを選ぶと、その .frq を表示します。");
            return;
        }

        var path = Path.Combine(_folder, Path.ChangeExtension(file.Name, ".frq"));
        if (!reload && string.Equals(path, _frqPath, StringComparison.OrdinalIgnoreCase) && FrqRoll.HasDocument)
            return;

        _frqPath = path;
        if (!File.Exists(path))
        {
            FrqRoll.SetDocument(null, 0, ".frq がありません。");
            return;
        }

        try
        {
            var document = FrqFile.Read(path);
            var rate = FrqFile.TryWavSampleRate(Path.Combine(_folder, file.Name)) ?? 0;
            var message = document.Frequency.Length == 0 ? ".frq に点がありません。" : null;
            FrqRoll.SetDocument(document, rate, message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            FrqRoll.SetDocument(null, 0, ex.Message);
        }
    }
}
