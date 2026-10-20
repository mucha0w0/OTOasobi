using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Text;

namespace Otoasobi;

public sealed class OtoParamSnapshot
{
    public string FileName { get; init; } = "";

    public string FileLabel { get; init; } = "";

    public int Index { get; init; }

    public string Alias { get; init; } = "";

    public string AliasLabel { get; init; } = "";

    public double Offset { get; init; }

    public double Overlap { get; init; }

    public double Preutter { get; init; }

    public double Consonant { get; init; }

    public double Blank { get; init; }

    public bool OffsetTuned { get; init; }

    public bool OverlapTuned { get; init; }

    public bool PreutterTuned { get; init; }

    public bool ConsonantTuned { get; init; }

    public bool BlankTuned { get; init; }
}

public sealed class OtoParamEdit
{
    public string FileName { get; init; } = "";

    public int Index { get; init; }

    public string Alias { get; init; } = "";

    public double Offset { get; init; }

    public double Overlap { get; init; }

    public double Preutter { get; init; }

    public double Consonant { get; init; }

    public double Blank { get; init; }
}

public sealed class OtoParamRow : INotifyPropertyChanged
{
    readonly bool[] _tuned = new bool[5];
    int _level = -1;
    Brush _fill = OtoParamTable.EmptyBrush;
    string _alias = "";
    string _aliasLabel = "";
    double _offset;
    double _overlap;
    double _preutter;
    double _consonant;
    double _blank;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FileName { get; init; } = "";

    public string FileLabel { get; init; } = "";

    public int Index { get; init; }

    public string Alias => _alias;

    public Brush EmphasisBrush => _fill;

    internal void SetLevel(int level, Brush fill)
    {
        if (_level == level)
            return;
        _level = level;
        _fill = fill;
        Raise(nameof(EmphasisBrush));
    }

    public string AliasLabel => _aliasLabel;

    internal int? EditingColumn { get; set; }

    public string OffsetText => OtoIni.Format(_offset);

    public string OverlapText => OtoIni.Format(_overlap);

    public string PreutterText => OtoIni.Format(_preutter);

    public string ConsonantText => OtoIni.Format(_consonant);

    public string BlankText => OtoIni.Format(_blank);

    public FontWeight OffsetWeight => Weight(0);

    public FontWeight OverlapWeight => Weight(1);

    public FontWeight PreutterWeight => Weight(2);

    public FontWeight ConsonantWeight => Weight(3);

    public FontWeight BlankWeight => Weight(4);

    public static OtoParamRow From(OtoParamSnapshot snap)
    {
        var row = new OtoParamRow
        {
            FileName = snap.FileName,
            FileLabel = snap.FileLabel,
            Index = snap.Index,
        };
        row.Pull(snap);
        return row;
    }

    public void Pull(OtoParamSnapshot snap)
    {
        var editing = EditingColumn;
        if (_alias != snap.Alias)
        {
            _alias = snap.Alias;
            Raise(nameof(Alias));
        }

        if (_aliasLabel != snap.AliasLabel)
        {
            _aliasLabel = snap.AliasLabel;
            Raise(nameof(AliasLabel));
        }

        Set(ref _offset, snap.Offset, snap.OffsetTuned, 0, editing, nameof(OffsetText), nameof(OffsetWeight));
        Set(ref _overlap, snap.Overlap, snap.OverlapTuned, 1, editing, nameof(OverlapText), nameof(OverlapWeight));
        Set(ref _preutter, snap.Preutter, snap.PreutterTuned, 2, editing, nameof(PreutterText), nameof(PreutterWeight));
        Set(ref _consonant, snap.Consonant, snap.ConsonantTuned, 3, editing, nameof(ConsonantText), nameof(ConsonantWeight));
        Set(ref _blank, snap.Blank, snap.BlankTuned, 4, editing, nameof(BlankText), nameof(BlankWeight));
    }

    public double[] Values() => [_offset, _overlap, _preutter, _consonant, _blank];

    public OtoParamEdit Edit(double offset, double overlap, double preutter, double consonant, double blank) =>
        new()
        {
            FileName = FileName,
            Index = Index,
            Alias = _alias,
            Offset = offset,
            Overlap = overlap,
            Preutter = preutter,
            Consonant = consonant,
            Blank = blank,
        };

    void Set(ref double field, double value, bool tuned, int column, int? editing, string textName, string weightName)
    {
        var weightChanged = _tuned[column] != tuned;
        _tuned[column] = tuned;
        var valueChanged = Math.Abs(field - value) >= 0.0005;
        field = value;
        if (weightChanged)
            Raise(weightName);
        if (valueChanged && editing != column)
            Raise(textName);
    }

    FontWeight Weight(int column) => _tuned[column] ? FontWeights.SemiBold : FontWeights.Normal;

    void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed partial class OtoParamTable : UserControl
{
    internal static readonly SolidColorBrush EmptyBrush = new(Color.FromArgb(0, 0, 0, 0));

    readonly ObservableCollection<OtoParamRow> _rows = [];
    readonly SolidColorBrush _fileFill;
    readonly SolidColorBrush _phonemeFill;
    ScrollViewer? _listScroll;
    string? _viewFile;
    int _focusIndex = -1;
    bool _silent;
    bool _commitLock;

    public OtoParamTable()
    {
        InitializeComponent();
        var accent = AccentColor();
        _fileFill = new SolidColorBrush(Color.FromArgb(48, accent.R, accent.G, accent.B));
        _phonemeFill = new SolidColorBrush(Color.FromArgb(150, accent.R, accent.G, accent.B));
        Rows.ItemsSource = _rows;
        Loaded += (_, _) => HookScroll();
    }

    public void SetEmphasis(string? fileName, int focusIndex)
    {
        var changed = focusIndex != _focusIndex
            || !string.Equals(fileName ?? "", _viewFile ?? "", StringComparison.OrdinalIgnoreCase);
        _viewFile = fileName;
        _focusIndex = focusIndex;
        ApplyEmphasis();
        SyncSelection(changed && focusIndex >= 0);
    }

    public event EventHandler<OtoParamRow>? RowChosen;

    public event EventHandler<IReadOnlyList<OtoParamEdit>>? ValuesEdited;

    public void Show(IReadOnlyList<OtoParamSnapshot> snaps, string emptyMessage)
    {
        HookScroll();
        EmptyText.Text = emptyMessage;
        var empty = snaps.Count == 0;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        Rows.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        if (snaps.Count == _rows.Count && SameKeys(snaps))
        {
            for (var i = 0; i < snaps.Count; i++)
                _rows[i].Pull(snaps[i]);
            ApplyEmphasis();
            SyncSelection(false);
            return;
        }

        var selected = Rows.SelectedItem as OtoParamRow;
        var offset = _listScroll?.VerticalOffset ?? 0;
        _rows.Clear();
        foreach (var snap in snaps)
            _rows.Add(OtoParamRow.From(snap));
        if (selected != null)
        {
            _silent = true;
            Rows.SelectedItem = _rows.FirstOrDefault(row =>
                row.Index == selected.Index
                && row.Alias == selected.Alias
                && row.FileName.Equals(selected.FileName, StringComparison.OrdinalIgnoreCase));
            _silent = false;
        }

        if (_listScroll != null && offset > 1)
            _listScroll.ChangeView(null, offset, null, true);
        ApplyEmphasis();
        SyncSelection(false);
    }

    void ApplyEmphasis()
    {
        foreach (var row in _rows)
        {
            var level = Level(row);
            row.SetLevel(level, Fill(level));
        }
    }

    int Level(OtoParamRow row)
    {
        if (string.IsNullOrEmpty(_viewFile) || !row.FileName.Equals(_viewFile, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (_focusIndex >= 0 && row.Index == _focusIndex)
            return 2;
        return 1;
    }

    Brush Fill(int level) => level switch
    {
        2 => _phonemeFill,
        1 => _fileFill,
        _ => EmptyBrush,
    };

    void SyncSelection(bool scroll)
    {
        if (string.IsNullOrEmpty(_viewFile) || _focusIndex < 0)
        {
            ClearSelection();
            return;
        }

        var row = _rows.FirstOrDefault(item =>
            item.Index == _focusIndex && item.FileName.Equals(_viewFile, StringComparison.OrdinalIgnoreCase));
        if (row == null)
            return;
        if (!ReferenceEquals(Rows.SelectedItem, row))
        {
            _silent = true;
            Rows.SelectedItem = row;
            _silent = false;
        }

        if (scroll)
            Rows.ScrollIntoView(row);
    }

    static Color AccentColor()
    {
        if (Application.Current?.Resources.TryGetValue("SystemAccentColor", out var value) == true && value is Color color)
            return color;
        return Color.FromArgb(255, 15, 157, 142);
    }

    public void Select(string fileName, int index)
    {
        var row = _rows.FirstOrDefault(item =>
            item.Index == index && item.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (row == null)
            return;
        if (!ReferenceEquals(Rows.SelectedItem, row))
        {
            _silent = true;
            Rows.SelectedItem = row;
            _silent = false;
        }

        Rows.ScrollIntoView(row);
    }

    public void ClearSelection()
    {
        if (Rows.SelectedItem == null)
            return;
        _silent = true;
        Rows.SelectedItem = null;
        _silent = false;
    }

    bool SameKeys(IReadOnlyList<OtoParamSnapshot> snaps)
    {
        for (var i = 0; i < snaps.Count; i++)
        {
            var row = _rows[i];
            var snap = snaps[i];
            if (row.Index != snap.Index || row.Alias != snap.Alias
                || !row.FileName.Equals(snap.FileName, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    void Rows_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_silent || Rows.SelectedItem is not OtoParamRow row)
            return;
        RowChosen?.Invoke(this, row);
    }

    void Row_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not OtoParamRow row)
            return;
        if (!e.GetCurrentPoint(element).Properties.IsLeftButtonPressed)
            return;
        if (ReferenceEquals(Rows.SelectedItem, row))
            RowChosen?.Invoke(this, row);
    }

    void Cell_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not OtoParamRow row)
            return;
        row.EditingColumn = Column(box);
        box.SelectAll();
        if (ReferenceEquals(Rows.SelectedItem, row))
            RowChosen?.Invoke(this, row);
        else
            Rows.SelectedItem = row;
    }

    void Cell_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not OtoParamRow row)
            return;
        row.EditingColumn = null;
        Commit(row, Column(box), box);
    }

    void Cell_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not OtoParamRow row)
            return;
        if (e.Key == VirtualKey.Escape)
        {
            box.Text = TextOf(row, Column(box));
            box.SelectAll();
            e.Handled = true;
            return;
        }

        var column = Column(box);
        var shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down)
            == CoreVirtualKeyStates.Down;
        int delta;
        if (e.Key == VirtualKey.Enter)
            delta = shift ? -1 : 1;
        else if (e.Key == VirtualKey.Down)
            delta = 1;
        else if (e.Key == VirtualKey.Up)
            delta = -1;
        else
            return;
        row.EditingColumn = null;
        Commit(row, column, box);
        Move(row, column, delta);
        e.Handled = true;
    }

    void Cell_Paste(object sender, TextControlPasteEventArgs e)
    {
        if (sender is not TextBox box)
            return;
        string? text;
        try
        {
            var view = Clipboard.GetContent();
            if (!view.Contains(StandardDataFormats.Text))
                return;
            var operation = view.GetTextAsync();
            if (operation.Status != Windows.Foundation.AsyncStatus.Completed)
            {
                e.Handled = true;
                var target = box;
                operation.Completed = (info, status) =>
                {
                    if (status != Windows.Foundation.AsyncStatus.Completed)
                        return;
                    var pasted = info.GetResults();
                    DispatcherQueue.TryEnqueue(() => ApplyPastedText(target, pasted));
                };
                return;
            }

            text = operation.GetResults();
        }
        catch (Exception)
        {
            return;
        }

        if (text == null || !IsBlock(text))
            return;
        e.Handled = true;
        ApplyPastedText(box, text);
    }

    void ApplyPastedText(TextBox box, string text)
    {
        if (!IsBlock(text))
        {
            Insert(box, text.Trim());
            return;
        }

        if (box.DataContext is not OtoParamRow origin)
            return;
        var edits = new List<OtoParamEdit>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
            lines = lines[..^1];
        var rowIndex = _rows.IndexOf(origin);
        var startCol = Column(box);
        foreach (var line in lines)
        {
            if (rowIndex < 0 || rowIndex >= _rows.Count)
                break;
            var row = _rows[rowIndex];
            rowIndex++;
            if (line.Length == 0)
                continue;
            if (!TryValues(row, line, startCol, out var values))
                continue;
            if (!Differs(row, values))
                continue;
            edits.Add(row.Edit(values[0], values[1], values[2], values[3], values[4]));
        }

        if (edits.Count == 0)
            return;
        foreach (var row in _rows)
            row.EditingColumn = null;
        ValuesEdited?.Invoke(this, edits);
    }

    static bool TryValues(OtoParamRow row, string line, int startCol, out double[] values)
    {
        values = row.Values();
        var cells = line.Split('\t');
        if (TryWholeRow(cells, out var whole))
        {
            values = whole;
            return true;
        }

        var any = false;
        for (var i = 0; i < cells.Length; i++)
        {
            var column = startCol + i;
            if (column is < 1 or > 5)
                continue;
            if (string.IsNullOrWhiteSpace(cells[i]) || !OtoIni.TryParse(cells[i], out var number))
                continue;
            values[column - 1] = OtoIni.Clamp(column - 1, number);
            any = true;
        }

        return any;
    }

    // 先頭が名前で、数値が 5 つある行は、setParam の 1 行として 5 欄へ入れる。
    static bool TryWholeRow(string[] cells, out double[] values)
    {
        values = new double[5];
        var numbers = new List<double>();
        var sawText = false;
        foreach (var cell in cells)
        {
            if (string.IsNullOrWhiteSpace(cell))
                continue;
            if (OtoIni.TryParse(cell, out var number))
            {
                numbers.Add(number);
                continue;
            }

            if (numbers.Count == 0)
            {
                sawText = true;
                continue;
            }

            break;
        }

        if (!sawText || numbers.Count != 5)
            return false;
        for (var i = 0; i < 5; i++)
            values[i] = OtoIni.Clamp(i, numbers[i]);
        return true;
    }

    void Commit(OtoParamRow row, int column, TextBox box)
    {
        if (_commitLock || column is < 1 or > 5)
            return;
        var values = row.Values();
        if (!OtoIni.TryParse(box.Text, out var number))
        {
            box.Text = TextOf(row, column);
            return;
        }

        values[column - 1] = OtoIni.Clamp(column - 1, number);
        if (!Differs(row, values))
        {
            box.Text = TextOf(row, column);
            return;
        }

        _commitLock = true;
        try
        {
            ValuesEdited?.Invoke(this, [row.Edit(values[0], values[1], values[2], values[3], values[4])]);
        }
        finally
        {
            _commitLock = false;
        }
    }

    static bool Differs(OtoParamRow row, double[] values)
    {
        var current = row.Values();
        for (var i = 0; i < 5; i++)
        {
            if (Math.Abs(current[i] - values[i]) >= 0.0005)
                return true;
        }

        return false;
    }

    void Move(OtoParamRow row, int column, int delta)
    {
        var index = _rows.IndexOf(row) + delta;
        if (index < 0 || index >= _rows.Count)
            return;
        var target = _rows[index];
        Rows.ScrollIntoView(target);
        Rows.UpdateLayout();
        if (Rows.ContainerFromItem(target) is not ListViewItem item)
            return;
        FindCell(item, column)?.Focus(FocusState.Programmatic);
    }

    static TextBox? FindCell(DependencyObject root, int column)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox box && Column(box) == column)
                return box;
            var found = FindCell(child, column);
            if (found != null)
                return found;
        }

        return null;
    }

    static string TextOf(OtoParamRow row, int column) => column switch
    {
        1 => row.OffsetText,
        2 => row.OverlapText,
        3 => row.PreutterText,
        4 => row.ConsonantText,
        _ => row.BlankText,
    };

    static int Column(TextBox box) =>
        box.Tag is string text && int.TryParse(text, out var column) ? column : -1;

    static bool IsBlock(string text)
    {
        var trimmed = text.TrimEnd('\r', '\n');
        return trimmed.Contains('\t') || trimmed.Contains('\n');
    }

    static void Insert(TextBox box, string text)
    {
        var current = box.Text ?? "";
        var start = Math.Clamp(box.SelectionStart, 0, current.Length);
        var length = Math.Clamp(box.SelectionLength, 0, current.Length - start);
        box.Text = string.Concat(current.AsSpan(0, start), text, current.AsSpan(start + length));
        box.SelectionStart = start + text.Length;
        box.SelectionLength = 0;
    }

    void HookScroll()
    {
        if (_listScroll != null)
            return;
        _listScroll = FindScrollViewer(Rows);
        if (_listScroll == null)
            return;
        _listScroll.ViewChanged += (_, _) =>
        {
            if (Math.Abs(HeaderScroll.HorizontalOffset - _listScroll.HorizontalOffset) < 0.5)
                return;
            HeaderScroll.ChangeView(_listScroll.HorizontalOffset, null, null, true);
        };
    }

    static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer)
            return viewer;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found != null)
                return found;
        }

        return null;
    }
}
