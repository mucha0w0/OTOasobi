using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace Otoasobi;

public enum FrqTool
{
    Select,
    Pen,
}

// 選択中の .frq を、鍵盤に沿った点で出す。高さは Hz のままなので、線のあいだにも置く。
public sealed class FrqPianoRoll : UserControl
{
    const double MinPixels = 3;
    const double MaxPixels = 28;

    readonly RollHit _hit = new();
    readonly Grid _host = new();
    readonly Image _image = new() { IsHitTestVisible = false, Stretch = Stretch.Fill };
    readonly Canvas _labels = new() { IsHitTestVisible = false };
    readonly TextBlock _message;
    readonly TextBlock _corner;
    readonly TextBlock _unvoiced;
    readonly ScrollBar _horizontal = new()
    {
        Orientation = Orientation.Horizontal,
        Height = 14,
        MinHeight = 14,
        MaxHeight = 14,
        ViewportSize = 1,
    };
    readonly ScrollBar _vertical = new()
    {
        Orientation = Orientation.Vertical,
        Width = 14,
        MinWidth = 14,
        MaxWidth = 14,
        ViewportSize = 1,
    };
    readonly InputCursor _arrow = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    readonly InputCursor _cross = InputSystemCursor.Create(InputSystemCursorShape.Cross);
    readonly PlotBuffer _buffer = new();
    readonly List<TextBlock> _notes = [];
    readonly List<TextBlock> _ticks = [];
    readonly List<(double X, string Text)> _tickMarks = [];
    readonly HashSet<int> _selected = [];
    readonly HashSet<int> _live = [];
    readonly List<int> _liveList = [];
    readonly SolidColorBrush _keyInk = new(Color.FromArgb(255, 55, 65, 81));
    readonly SolidColorBrush _tickInk = new(Color.FromArgb(255, 209, 213, 219));

    FrqFile? _file;
    double[]? _undo;
    int _rate;
    string _messageText = "ファイルを選ぶと、その .frq を表示します。";
    FrqTool _tool = FrqTool.Select;
    double _ppf = 12;
    double _scrollX;
    double _scrollY;
    double _laneTop = FrqPitch.Ruler;
    bool _fitted;
    bool _dragging;
    bool _dirty;
    bool _barSync;
    bool _columnDrag;
    bool _labelsReady;
    string _labelKey = "";
    SheetPoint _anchor;
    SheetPoint _current;
    PenSample _pen;
    WriteableBitmap? _bitmap;
    int _midiLow = FrqPitch.MidiLow;
    int _midiHigh = FrqPitch.MidiHigh;

    Color _canvas = Color.FromArgb(255, 22, 22, 22);
    Color _whiteKey = Color.FromArgb(255, 228, 228, 231);
    Color _blackKey = Color.FromArgb(255, 38, 38, 42);
    Color _whiteRow = Color.FromArgb(255, 32, 32, 34);
    Color _blackRow = Color.FromArgb(255, 18, 18, 20);
    Color _grid = Color.FromArgb(255, 55, 55, 60);
    Color _gridC = Color.FromArgb(255, 90, 90, 98);
    Color _lane = Color.FromArgb(255, 28, 28, 30);
    Color _point = Color.FromArgb(255, 45, 212, 191);
    Color _pointEdge = Color.FromArgb(255, 12, 18, 22);
    Color _selectedPoint = Color.FromArgb(255, 251, 191, 36);
    Color _unvoicedPoint = Color.FromArgb(255, 148, 163, 184);
    Color _selectFill = Color.FromArgb(56, 15, 157, 142);
    Color _selectStroke = Color.FromArgb(255, 15, 157, 142);
    Color _line = Color.FromArgb(255, 80, 80, 88);

    public FrqPianoRoll()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _message = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            IsHitTestVisible = false,
            Margin = new Thickness(FrqPitch.KeyWidth + 8, FrqPitch.Ruler, 12, FrqPitch.Unvoiced + 8),
            Text = _messageText,
        };
        if (Application.Current?.Resources.TryGetValue("CaptionTextBlockStyle", out var caption) == true && caption is Style captionStyle)
            _message.Style = captionStyle;
        _corner = new TextBlock { FontSize = 11, IsHitTestVisible = false, Foreground = _keyInk };
        _unvoiced = new TextBlock
        {
            Text = "なし",
            FontSize = 11,
            Width = FrqPitch.KeyWidth,
            Height = FrqPitch.Unvoiced,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Foreground = _keyInk,
            Padding = new Thickness(0, 3, 0, 0),
        };
        _labels.Children.Add(_corner);
        _labels.Children.Add(_unvoiced);
        Canvas.SetLeft(_corner, 16);
        Canvas.SetTop(_corner, 2);
        _host.Children.Add(_image);
        _host.Children.Add(_labels);
        _host.Children.Add(_message);
        _hit.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        _hit.SetCursor(_arrow);
        _host.Children.Add(_hit);
        grid.Children.Add(_host);
        Grid.SetColumn(_vertical, 1);
        grid.Children.Add(_vertical);
        Grid.SetRow(_horizontal, 1);
        grid.Children.Add(_horizontal);
        Content = grid;

        _hit.PointerPressed += Pressed;
        _hit.PointerMoved += Moved;
        _hit.PointerReleased += Released;
        _hit.PointerCanceled += Released;
        _hit.PointerCaptureLost += CaptureLost;
        _horizontal.ValueChanged += HorizontalChanged;
        _vertical.ValueChanged += VerticalChanged;
        SizeChanged += (_, _) => OnSize();
        ActualThemeChanged += (_, _) =>
        {
            ApplyTheme();
            _labelKey = "";
            Paint();
        };
        IsEnabledChanged += (_, _) =>
        {
            if (!IsEnabled)
                Cancel();
        };
        Loaded += (_, _) =>
        {
            ApplyTheme();
            OnSize();
        };
        ApplyTheme();
        IsTabStop = false;
    }

    public event EventHandler? Edited;

    public event EventHandler? SelectionChanged;

    public FrqFile? Document => _file;

    public bool HasDocument => _file != null;

    public int SelectionCount => _selected.Count;

    public FrqTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value)
                return;
            Flush();
            _tool = value;
            _hit.SetCursor(value == FrqTool.Pen ? _cross : _arrow);
        }
    }

    public void SetDocument(FrqFile? file, int sampleRate, string? message)
    {
        _dragging = false;
        _undo = null;
        _dirty = false;
        _file = file;
        _rate = sampleRate;
        _messageText = message ?? "";
        _selected.Clear();
        _live.Clear();
        _liveList.Clear();
        _fitted = false;
        _labelKey = "";
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        OnSize();
    }

    public void SetColumnDrag(bool dragging)
    {
        if (dragging)
        {
            if (_columnDrag)
                return;
            _columnDrag = true;
            Opacity = 0.55;
            return;
        }

        if (!_columnDrag)
            return;
        _columnDrag = false;
        Opacity = 1;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!_columnDrag)
                OnSize();
        });
    }

    public void Flush()
    {
        if (_dragging && _tool == FrqTool.Pen)
        {
            _dragging = false;
            Commit();
            Paint();
            return;
        }

        if (_dragging)
        {
            _dragging = false;
            FinishSelect();
            Paint();
            return;
        }

        if (_dirty)
            Edited?.Invoke(this, EventArgs.Empty);
    }

    public void Cancel()
    {
        var paint = _dragging || _undo != null;
        _dragging = false;
        _live.Clear();
        _liveList.Clear();
        if (_undo != null && _file != null && _undo.Length == _file.Frequency.Length)
        {
            _undo.CopyTo(_file.Frequency, 0);
            _dirty = false;
        }

        _undo = null;
        if (paint)
            Paint();
    }

    public void MarkClean() => _dirty = false;

    public void ShiftSelection(int octaves)
    {
        if (_file == null || _selected.Count == 0)
            return;
        if (!FrqPitch.ShiftOctaves(_file.Frequency, _selected, octaves))
            return;
        RevealMedian();
        NotifyEdited();
    }

    public void ClearSelectedPitch()
    {
        if (_file == null || _selected.Count == 0)
            return;
        if (!FrqPitch.Clear(_file.Frequency, _selected))
            return;
        NotifyEdited();
    }

    protected override void OnPointerWheelChanged(PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        if (delta == 0 || _dragging)
        {
            e.Handled = true;
            return;
        }

        var x = e.GetCurrentPoint(_host).Position.X;
        if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control))
            Zoom(x, delta > 0 ? 1.25 : 1 / 1.25);
        else if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift))
            ScrollBy(delta > 0 ? -80 : 80, 0);
        else
            ScrollBy(0, delta > 0 ? -48 : 48);
        e.Handled = true;
    }

    void NotifyEdited()
    {
        _dirty = true;
        Paint();
        Edited?.Invoke(this, EventArgs.Empty);
    }

    void Commit()
    {
        _undo = null;
        if (!_dirty)
            return;
        Edited?.Invoke(this, EventArgs.Empty);
    }

    void Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (_file == null || _file.Frequency.Length == 0 || !IsEnabled)
            return;
        var point = e.GetCurrentPoint(_host);
        if (!point.Properties.IsLeftButtonPressed)
            return;
        var position = point.Position;
        if (position.X < FrqPitch.KeyWidth || position.Y < FrqPitch.Ruler)
            return;
        _dragging = true;
        _hit.CapturePointer(e.Pointer);
        if (_tool == FrqTool.Pen)
        {
            if (_selected.Count > 0)
            {
                _selected.Clear();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }

            _undo = _file.Frequency.ToArray();
            _pen = SampleAt(position);
            FrqPitch.Stroke(
                _file.Frequency,
                _pen.Frame, _pen.Midi, _pen.Unvoiced,
                _pen.Frame, _pen.Midi, _pen.Unvoiced);
            _dirty = true;
        }
        else
        {
            _anchor = ToSheet(position);
            _current = _anchor;
            UpdateLive();
        }

        Paint();
        e.Handled = true;
    }

    void Moved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || _file == null)
            return;
        var position = e.GetCurrentPoint(_host).Position;
        Nudge(position);
        if (_tool == FrqTool.Pen)
        {
            var sample = SampleAt(position);
            FrqPitch.Stroke(
                _file.Frequency,
                _pen.Frame, _pen.Midi, _pen.Unvoiced,
                sample.Frame, sample.Midi, sample.Unvoiced);
            _pen = sample;
            _dirty = true;
        }
        else
        {
            _current = ToSheet(position);
            UpdateLive();
        }

        Paint();
        e.Handled = true;
    }

    void Released(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
            return;
        _dragging = false;
        if (_hit.PointerCaptures.Any(captured => captured.PointerId == e.Pointer.PointerId))
            _hit.ReleasePointerCapture(e.Pointer);
        if (_tool == FrqTool.Pen)
            Commit();
        else
            FinishSelect();
        Paint();
        e.Handled = true;
    }

    void CaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
            return;
        _dragging = false;
        if (_tool == FrqTool.Pen)
            Commit();
        else
            FinishSelect();
        Paint();
    }

    void FinishSelect()
    {
        var start = ToViewport(_anchor);
        var end = ToViewport(_current);
        var tiny = Math.Abs(start.X - end.X) < 4 && Math.Abs(start.Y - end.Y) < 4;
        _selected.Clear();
        if (_file != null && tiny)
        {
            if (Nearest(end.X, end.Y) is int index)
                _selected.Add(index);
        }
        else
        {
            foreach (var index in _liveList)
                _selected.Add(index);
        }

        _live.Clear();
        _liveList.Clear();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    void UpdateLive()
    {
        _live.Clear();
        _liveList.Clear();
        if (_file == null)
            return;
        var start = ToViewport(_anchor);
        var end = ToViewport(_current);
        View().Collect(_file.Frequency, start.X, start.Y, end.X, end.Y, _liveList);
        foreach (var index in _liveList)
            _live.Add(index);
    }

    int? Nearest(double x, double y)
    {
        if (_file == null)
            return null;
        var view = View();
        var frame = view.FrameAt(x);
        var best = -1;
        var bestDistance = 64.0;
        var count = _file.Frequency.Length;
        for (var i = frame - 3; i <= frame + 3; i++)
        {
            if ((uint)i >= (uint)count)
                continue;
            var px = view.ViewportX(i);
            var hz = _file.Frequency[i];
            double py;
            if (hz > 0 && double.IsFinite(hz))
            {
                py = view.ViewportY(FrqPitch.ToMidi(hz));
                if (py < FrqPitch.Ruler || py >= _laneTop)
                    continue;
            }
            else
            {
                py = _laneTop + FrqPitch.Unvoiced / 2;
            }

            var distance = (px - x) * (px - x) + (py - y) * (py - y);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best >= 0 ? best : null;
    }

    PenSample SampleAt(Point position)
    {
        var frame = Math.Clamp(View().FrameAt(position.X), 0, _file!.Frequency.Length - 1);
        if (position.Y >= _laneTop)
            return new PenSample(frame, 0, true);
        var y = Math.Clamp(position.Y, FrqPitch.Ruler, Math.Max(FrqPitch.Ruler, _laneTop - 0.5));
        var midi = Math.Clamp(View().MidiAt(y), _midiLow - 0.5, _midiHigh + 0.5);
        return new PenSample(frame, midi, false);
    }

    SheetPoint ToSheet(Point position)
    {
        var x = position.X + _scrollX;
        if (position.Y >= _laneTop)
            return new SheetPoint(x, position.Y, true);
        var y = Math.Clamp(position.Y, FrqPitch.Ruler, Math.Max(FrqPitch.Ruler, _laneTop - 0.5));
        return new SheetPoint(x, y + _scrollY, false);
    }

    Point ToViewport(SheetPoint sheet)
    {
        var x = sheet.X - _scrollX;
        var y = sheet.Lane ? sheet.Y : sheet.Y - _scrollY;
        return new Point(x, y);
    }

    void Nudge(Point position)
    {
        const double edge = 20;
        const double step = 18;
        var moved = false;
        if (position.X > FrqPitch.KeyWidth && position.X < FrqPitch.KeyWidth + edge)
        {
            _scrollX -= step;
            moved = true;
        }
        else if (position.X > _host.ActualWidth - edge)
        {
            _scrollX += step;
            moved = true;
        }

        if (position.Y >= FrqPitch.Ruler && position.Y < _laneTop)
        {
            if (position.Y < FrqPitch.Ruler + edge)
            {
                _scrollY -= step;
                moved = true;
            }
            else if (position.Y > _laneTop - edge)
            {
                _scrollY += step;
                moved = true;
            }
        }

        if (!moved)
            return;
        ClampScroll();
        SyncBars();
    }

    void RevealMedian()
    {
        if (_file == null || _selected.Count == 0)
            return;
        var midis = new List<double>();
        foreach (var index in _selected)
        {
            var hz = _file.Frequency[index];
            if (hz > 0 && double.IsFinite(hz))
                midis.Add(FrqPitch.ToMidi(hz));
        }

        if (midis.Count == 0)
            return;
        midis.Sort();
        var midi = midis[midis.Count / 2];
        var viewHeight = Math.Max(1, _host.ActualHeight - FrqPitch.Ruler - FrqPitch.Unvoiced);
        var y = FrqPitch.Ruler + (_midiHigh - midi + 0.5) * FrqPitch.Row;
        var top = FrqPitch.Ruler + _scrollY;
        if (y < top + FrqPitch.Row || y > top + viewHeight - FrqPitch.Row)
            _scrollY = y - FrqPitch.Ruler - viewHeight / 2;
        ClampScroll();
        SyncBars();
    }

    void Zoom(double viewportX, double factor)
    {
        if (_file == null || _file.Frequency.Length == 0 || _ppf <= 0)
            return;
        var next = Math.Clamp(_ppf * factor, MinPixels, MaxPixels);
        if (Math.Abs(next - _ppf) < 0.01)
            return;
        var frames = (viewportX + _scrollX - FrqPitch.KeyWidth) / _ppf;
        _ppf = next;
        _scrollX = FrqPitch.KeyWidth + frames * next - viewportX;
        ClampScroll();
        SyncBars();
        Paint();
    }

    void ScrollBy(double x, double y)
    {
        _scrollX += x;
        _scrollY += y;
        ClampScroll();
        SyncBars();
        Paint();
    }

    void HorizontalChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_barSync)
            return;
        _scrollX = e.NewValue;
        Paint();
    }

    void VerticalChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_barSync)
            return;
        _scrollY = e.NewValue;
        Paint();
    }

    void OnSize()
    {
        if (_columnDrag)
            return;
        _laneTop = Math.Max(FrqPitch.Ruler, _host.ActualHeight - FrqPitch.Unvoiced);
        FitIfNeeded();
        ClampScroll();
        SyncBars();
        Paint();
    }

    void FitIfNeeded()
    {
        if (_fitted || _host.ActualWidth < 2 || _host.ActualHeight < 2)
            return;
        _fitted = true;
        var count = _file?.Frequency.Length ?? 0;
        var plotWidth = Math.Max(1, _host.ActualWidth - FrqPitch.KeyWidth);
        _ppf = count <= 1 ? 12 : Math.Clamp(plotWidth / count, MinPixels, 24);
        _scrollX = 0;
        var midi = 60.0;
        if (_file != null)
        {
            var midis = new List<double>();
            foreach (var hz in _file.Frequency)
            {
                if (hz > 0 && double.IsFinite(hz))
                    midis.Add(FrqPitch.ToMidi(hz));
            }

            if (midis.Count > 0)
            {
                midis.Sort();
                midi = midis[midis.Count / 2];
            }
        }

        var viewHeight = Math.Max(1, _host.ActualHeight - FrqPitch.Ruler - FrqPitch.Unvoiced);
        var y = FrqPitch.Ruler + (_midiHigh - midi + 0.5) * FrqPitch.Row;
        _scrollY = y - FrqPitch.Ruler - viewHeight / 2;
    }

    void ClampScroll()
    {
        var count = _file?.Frequency.Length ?? 0;
        var plotWidth = Math.Max(1, _host.ActualWidth - FrqPitch.KeyWidth);
        var viewHeight = Math.Max(1, _host.ActualHeight - FrqPitch.Ruler - FrqPitch.Unvoiced);
        var gridHeight = (_midiHigh - _midiLow + 1) * FrqPitch.Row;
        _scrollX = Math.Clamp(_scrollX, 0, Math.Max(0, count * _ppf - plotWidth));
        _scrollY = Math.Clamp(_scrollY, 0, Math.Max(0, gridHeight - viewHeight));
    }

    void SyncBars()
    {
        var count = _file?.Frequency.Length ?? 0;
        var plotWidth = Math.Max(1, _host.ActualWidth - FrqPitch.KeyWidth);
        var viewHeight = Math.Max(1, _host.ActualHeight - FrqPitch.Ruler - FrqPitch.Unvoiced);
        var gridHeight = (_midiHigh - _midiLow + 1) * FrqPitch.Row;
        var maxX = Math.Max(0, count * _ppf - plotWidth);
        var maxY = Math.Max(0, gridHeight - viewHeight);
        _barSync = true;
        SetBar(_horizontal, _scrollX, maxX, plotWidth);
        SetBar(_vertical, _scrollY, maxY, viewHeight);
        _barSync = false;
    }

    static void SetBar(ScrollBar bar, double value, double maximum, double viewport)
    {
        bar.Value = 0;
        bar.Minimum = 0;
        bar.ViewportSize = 0;
        bar.Maximum = maximum <= 0 ? 0 : maximum;
        bar.ViewportSize = maximum <= 0 ? 0 : viewport;
        bar.SmallChange = Math.Max(viewport * 0.1, 1);
        bar.LargeChange = Math.Max(viewport * 0.5, 1);
        bar.Value = maximum <= 0 ? 0 : Math.Clamp(value, 0, maximum);
        bar.IsEnabled = maximum > 0.5;
    }

    FrqView View() => new()
    {
        MidiLow = _midiLow,
        MidiHigh = _midiHigh,
        PixelsPerFrame = _ppf,
        ScrollX = _scrollX,
        ScrollY = _scrollY,
        LaneTop = _laneTop,
    };

    void Paint()
    {
        if (_columnDrag)
            return;
        var width = _host.ActualWidth;
        var height = _host.ActualHeight;
        _message.Text = _messageText;
        _message.Visibility = string.IsNullOrEmpty(_messageText) ? Visibility.Collapsed : Visibility.Visible;
        if (width < 2 || height < 2)
            return;
        _laneTop = Math.Max(FrqPitch.Ruler, height - FrqPitch.Unvoiced);
        var scale = XamlRoot?.RasterizationScale ?? 1;
        if (scale < 1)
            scale = 1;
        var pixelWidth = (int)Math.Ceiling(width * scale);
        var pixelHeight = (int)Math.Ceiling(height * scale);
        if (pixelWidth > 8192)
            pixelWidth = 8192;
        if (pixelHeight > 8192)
            pixelHeight = 8192;
        if (_bitmap == null || _bitmap.PixelWidth != pixelWidth || _bitmap.PixelHeight != pixelHeight)
        {
            _bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
            _image.Source = _bitmap;
        }

        _buffer.Begin(pixelWidth, pixelHeight, scale);
        _buffer.Fill(0, 0, width, height, _canvas);
        _buffer.Fill(0, 0, FrqPitch.KeyWidth, height, _whiteKey);
        PaintRows(width);
        _buffer.Fill(0, _laneTop, FrqPitch.KeyWidth, height - _laneTop, _whiteKey);
        _buffer.Fill(FrqPitch.KeyWidth, _laneTop, width - FrqPitch.KeyWidth, height - _laneTop, _lane);
        PaintLines(width);
        _buffer.VLine(FrqPitch.KeyWidth, 0, height, _line);
        _buffer.HLine(FrqPitch.KeyWidth, FrqPitch.Ruler, width - FrqPitch.KeyWidth, _line);
        _buffer.HLine(0, _laneTop, width, _line);
        if (_dragging && _tool == FrqTool.Select)
        {
            var start = ToViewport(_anchor);
            var end = ToViewport(_current);
            var x = Math.Min(start.X, end.X);
            var y = Math.Min(start.Y, end.Y);
            var rectWidth = Math.Abs(end.X - start.X);
            var rectHeight = Math.Abs(end.Y - start.Y);
            _buffer.Fill(x, y, rectWidth, rectHeight, _selectFill);
            _buffer.Stroke(x, y, rectWidth, rectHeight, _selectStroke);
        }

        PaintPoints(width);
        _buffer.Present(_bitmap);
        LayoutLabels(width);
    }

    void PaintRows(double width)
    {
        var view = View();
        var high = Math.Clamp((int)Math.Ceiling(view.MidiAt(FrqPitch.Ruler)), _midiLow, _midiHigh);
        var low = Math.Clamp((int)Math.Floor(view.MidiAt(_laneTop)), _midiLow, _midiHigh);
        for (var midi = low; midi <= high; midi++)
        {
            var top = FrqPitch.Ruler + (_midiHigh - midi) * FrqPitch.Row - _scrollY;
            var bottom = Math.Min(top + FrqPitch.Row, _laneTop);
            top = Math.Max(top, FrqPitch.Ruler);
            if (bottom <= top)
                continue;
            var black = FrqPitch.IsBlack(midi);
            _buffer.Fill(FrqPitch.KeyWidth, top, width - FrqPitch.KeyWidth, bottom - top, black ? _blackRow : _whiteRow);
            if (!black)
            {
                _buffer.Fill(0, top, FrqPitch.KeyWidth, bottom - top, _whiteKey);
                continue;
            }

            var inset = Math.Min(1, (bottom - top) / 4);
            _buffer.Fill(0, top + inset, FrqPitch.KeyWidth * 0.62, Math.Max(1, bottom - top - inset * 2), _blackKey);
        }
    }

    void PaintLines(double width)
    {
        var view = View();
        var high = Math.Clamp((int)Math.Ceiling(view.MidiAt(FrqPitch.Ruler)), _midiLow, _midiHigh);
        var low = Math.Clamp((int)Math.Floor(view.MidiAt(_laneTop)), _midiLow, _midiHigh);
        for (var midi = low; midi <= high; midi++)
        {
            var y = FrqPitch.Ruler + (_midiHigh - midi) * FrqPitch.Row - _scrollY;
            if (y < FrqPitch.Ruler || y >= _laneTop)
                continue;
            _buffer.HLine(FrqPitch.KeyWidth, y, width - FrqPitch.KeyWidth, midi % 12 == 0 ? _gridC : _grid);
        }

        BuildTicks(width);
        foreach (var tick in _tickMarks)
        {
            if (tick.X < FrqPitch.KeyWidth || tick.X > width)
                continue;
            _buffer.VLine(tick.X, FrqPitch.Ruler, _laneTop - FrqPitch.Ruler, _grid);
            _buffer.VLine(tick.X, FrqPitch.Ruler - 6, 6, _gridC);
        }
    }

    void PaintPoints(double width)
    {
        if (_file == null || _file.Frequency.Length == 0)
            return;
        var view = View();
        var count = _file.Frequency.Length;
        var first = Math.Max(0, view.FrameAt(FrqPitch.KeyWidth) - 1);
        var last = Math.Min(count - 1, view.FrameAt(width) + 1);
        var radius = Math.Clamp(_ppf * 0.38, 1.8, 4.2);
        var highlight = _dragging && _tool == FrqTool.Select ? _live : _selected;
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = first; i <= last; i++)
            {
                var chosen = highlight.Contains(i);
                if (chosen != (pass == 1))
                    continue;
                var hz = _file.Frequency[i];
                var x = view.ViewportX(i);
                if (x < FrqPitch.KeyWidth - radius || x > width + radius)
                    continue;
                double y;
                Color fill;
                if (!(hz > 0) || !double.IsFinite(hz))
                {
                    y = _laneTop + FrqPitch.Unvoiced / 2;
                    fill = chosen ? _selectedPoint : _unvoicedPoint;
                }
                else
                {
                    y = view.ViewportY(FrqPitch.ToMidi(hz));
                    if (y < FrqPitch.Ruler - radius || y > _laneTop)
                        continue;
                    fill = chosen ? _selectedPoint : _point;
                }

                if (!chosen && radius >= 2.2)
                    _buffer.Circle(x, y, radius + 0.9, _pointEdge);
                _buffer.Circle(x, y, chosen ? radius + 0.7 : radius, fill);
            }
        }
    }

    void BuildTicks(double width)
    {
        _tickMarks.Clear();
        if (_file == null || _ppf <= 0)
            return;
        if (_rate > 0 && _file.Hop > 0)
        {
            var secondsPerPixel = (_file.Hop / (double)_rate) / _ppf;
            var step = NiceStep(secondsPerPixel * 72, [0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 30, 60]);
            var start = Math.Floor(TimeAt(FrqPitch.KeyWidth) / step) * step;
            var end = TimeAt(width) + step;
            for (var time = Math.Max(0, start); time <= end + 1e-6; time += step)
            {
                var x = FrqPitch.KeyWidth + time * _rate / _file.Hop * _ppf - _scrollX;
                if (x < FrqPitch.KeyWidth - 1 || x > width + 1)
                    continue;
                _tickMarks.Add((x, FormatTime(time)));
            }

            return;
        }

        var framesPerPixel = 1 / _ppf;
        var frameStep = (int)NiceStep(framesPerPixel * 72, [1, 2, 5, 10, 20, 50, 100, 200, 500, 1000]);
        frameStep = Math.Max(1, frameStep);
        var first = Math.Max(0, (int)Math.Floor(View().FrameAt(FrqPitch.KeyWidth) / (double)frameStep) * frameStep);
        var lastFrame = View().FrameAt(width) + frameStep;
        for (var frame = first; frame <= lastFrame; frame += frameStep)
        {
            var x = FrqPitch.KeyWidth + frame * _ppf - _scrollX;
            if (x < FrqPitch.KeyWidth - 1 || x > width + 1)
                continue;
            _tickMarks.Add((x, frame.ToString()));
        }
    }

    double TimeAt(double viewportX)
    {
        if (_file == null || _rate <= 0 || _file.Hop <= 0 || _ppf <= 0)
            return 0;
        var frame = (viewportX + _scrollX - FrqPitch.KeyWidth) / _ppf;
        return frame * _file.Hop / _rate;
    }

    static double NiceStep(double target, double[] steps)
    {
        foreach (var step in steps)
        {
            if (step >= target - 1e-9)
                return step;
        }

        return steps[^1];
    }

    static string FormatTime(double seconds)
    {
        if (Math.Abs(seconds - Math.Round(seconds)) < 0.001)
            return Math.Round(seconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
    }

    void LayoutLabels(double width)
    {
        var key = FormattableString.Invariant(
            $"{_scrollX:0.##}|{_scrollY:0.##}|{_ppf:0.##}|{width:0}|{_host.ActualHeight:0}|{_laneTop:0}|{_rate}|{_file?.Hop}|{_tickMarks.Count}|{_tickInk.Color.R}");
        Canvas.SetTop(_unvoiced, _laneTop);
        _corner.Text = _rate > 0 && _file is { Hop: > 0 } ? "秒" : "点";
        if (key == _labelKey && _labelsReady)
            return;
        _labelKey = key;
        _labelsReady = true;
        var used = 0;
        var view = View();
        var high = Math.Clamp((int)Math.Ceiling(view.MidiAt(FrqPitch.Ruler)), _midiLow, _midiHigh);
        var low = Math.Clamp((int)Math.Floor(view.MidiAt(_laneTop)), _midiLow, _midiHigh);
        for (var midi = high; midi >= low; midi--)
        {
            if (midi % 12 != 0)
                continue;
            var y = FrqPitch.Ruler + (_midiHigh - midi + 0.5) * FrqPitch.Row - _scrollY;
            if (y < FrqPitch.Ruler || y > _laneTop)
                continue;
            var block = Note(used++);
            block.Text = FrqPitch.NoteLabel(midi);
            block.Visibility = Visibility.Visible;
            Canvas.SetLeft(block, 2);
            Canvas.SetTop(block, y - 7);
        }

        for (var i = used; i < _notes.Count; i++)
            _notes[i].Visibility = Visibility.Collapsed;

        var tickUsed = 0;
        foreach (var tick in _tickMarks)
        {
            if (tick.X < FrqPitch.KeyWidth || tick.X > width - 8)
                continue;
            var block = Tick(tickUsed++);
            block.Text = tick.Text;
            block.Visibility = Visibility.Visible;
            Canvas.SetLeft(block, tick.X + 3);
            Canvas.SetTop(block, 2);
        }

        for (var i = tickUsed; i < _ticks.Count; i++)
            _ticks[i].Visibility = Visibility.Collapsed;
    }

    TextBlock Note(int index)
    {
        while (_notes.Count <= index)
        {
            var block = new TextBlock
            {
                FontSize = 10,
                Width = FrqPitch.KeyWidth - 8,
                TextAlignment = TextAlignment.Right,
                IsHitTestVisible = false,
                Foreground = _keyInk,
            };
            _notes.Add(block);
            _labels.Children.Add(block);
        }

        return _notes[index];
    }

    TextBlock Tick(int index)
    {
        while (_ticks.Count <= index)
        {
            var block = new TextBlock
            {
                FontSize = 10,
                IsHitTestVisible = false,
                Foreground = _tickInk,
            };
            _ticks.Add(block);
            _labels.Children.Add(block);
        }

        return _ticks[index];
    }

    void ApplyTheme()
    {
        var light = AppearanceTheme.Resolve(this) == ElementTheme.Light;
        if (light)
        {
            _canvas = Color.FromArgb(255, 244, 244, 245);
            _whiteKey = Color.FromArgb(255, 255, 255, 255);
            _blackKey = Color.FromArgb(255, 55, 55, 62);
            _whiteRow = Color.FromArgb(255, 250, 250, 251);
            _blackRow = Color.FromArgb(255, 232, 232, 236);
            _grid = Color.FromArgb(255, 214, 214, 219);
            _gridC = Color.FromArgb(255, 156, 163, 175);
            _lane = Color.FromArgb(255, 236, 236, 240);
            _point = Color.FromArgb(255, 13, 148, 136);
            _pointEdge = Color.FromArgb(255, 255, 255, 255);
            _selectedPoint = Color.FromArgb(255, 180, 83, 9);
            _unvoicedPoint = Color.FromArgb(255, 100, 116, 139);
            _line = Color.FromArgb(255, 180, 180, 188);
            _keyInk.Color = Color.FromArgb(255, 55, 65, 81);
            _tickInk.Color = Color.FromArgb(255, 75, 85, 99);
        }
        else
        {
            _canvas = Color.FromArgb(255, 22, 22, 22);
            _whiteKey = Color.FromArgb(255, 228, 228, 231);
            _blackKey = Color.FromArgb(255, 42, 42, 48);
            _whiteRow = Color.FromArgb(255, 32, 32, 36);
            _blackRow = Color.FromArgb(255, 16, 16, 18);
            _grid = Color.FromArgb(255, 58, 58, 64);
            _gridC = Color.FromArgb(255, 120, 120, 128);
            _lane = Color.FromArgb(255, 30, 30, 34);
            _point = Color.FromArgb(255, 45, 212, 191);
            _pointEdge = Color.FromArgb(255, 15, 23, 28);
            _selectedPoint = Color.FromArgb(255, 251, 191, 36);
            _unvoicedPoint = Color.FromArgb(255, 148, 163, 184);
            _line = Color.FromArgb(255, 82, 82, 90);
            _keyInk.Color = Color.FromArgb(255, 55, 65, 81);
            _tickInk.Color = Color.FromArgb(255, 209, 213, 219);
        }

        var accent = AppearanceTheme.Brush(AppearanceTheme.Resolve(this), "AccentFillColorDefaultBrush") as SolidColorBrush;
        var accentColor = accent?.Color ?? Color.FromArgb(255, 15, 157, 142);
        _selectStroke = accentColor;
        _selectFill = Color.FromArgb(48, accentColor.R, accentColor.G, accentColor.B);
        var secondary = AppearanceTheme.Brush(AppearanceTheme.Resolve(this), "TextFillColorSecondaryBrush");
        if (secondary != null)
            _message.Foreground = secondary;
        _host.Background = new SolidColorBrush(_canvas);
        foreach (var block in _notes)
            block.Foreground = _keyInk;
        foreach (var block in _ticks)
            block.Foreground = _tickInk;
        _corner.Foreground = _keyInk;
        _unvoiced.Foreground = _keyInk;
    }

    readonly record struct SheetPoint(double X, double Y, bool Lane);

    readonly record struct PenSample(int Frame, double Midi, bool Unvoiced);

    sealed class RollHit : Canvas
    {
        public void SetCursor(InputCursor cursor) => ProtectedCursor = cursor;
    }

    sealed class PlotBuffer
    {
        byte[] _pixels = [];
        byte[] _row = [];
        int _width;
        int _height;
        double _scale = 1;

        public void Begin(int width, int height, double scale)
        {
            var bytes = width * height * 4;
            if (_pixels.Length != bytes)
                _pixels = new byte[bytes];
            _width = width;
            _height = height;
            _scale = scale;
        }

        public void Fill(double x, double y, double width, double height, Color color)
        {
            if (color.A == 0 || width <= 0 || height <= 0)
                return;
            var left = (int)Math.Floor(x * _scale);
            var top = (int)Math.Floor(y * _scale);
            var right = (int)Math.Ceiling((x + width) * _scale);
            var bottom = (int)Math.Ceiling((y + height) * _scale);
            if (left < 0)
                left = 0;
            if (top < 0)
                top = 0;
            if (right > _width)
                right = _width;
            if (bottom > _height)
                bottom = _height;
            if (right <= left || bottom <= top)
                return;
            if (color.A == 255)
            {
                var span = right - left;
                EnsureRow(span, color);
                var bytes = span * 4;
                for (var row = top; row < bottom; row++)
                    Buffer.BlockCopy(_row, 0, _pixels, (row * _width + left) * 4, bytes);
                return;
            }

            for (var row = top; row < bottom; row++)
            {
                for (var column = left; column < right; column++)
                    Blend(column, row, color);
            }
        }

        public void HLine(double x, double y, double width, Color color)
        {
            var pixel = 1 / _scale;
            Fill(x, y, width, pixel, color);
        }

        public void VLine(double x, double y, double height, Color color)
        {
            var pixel = 1 / _scale;
            Fill(x, y, pixel, height, color);
        }

        public void Stroke(double x, double y, double width, double height, Color color)
        {
            var pixel = 1 / _scale;
            Fill(x, y, width, pixel, color);
            Fill(x, y + height - pixel, width, pixel, color);
            Fill(x, y, pixel, height, color);
            Fill(x + width - pixel, y, pixel, height, color);
        }

        public void Circle(double centerX, double centerY, double radius, Color color)
        {
            var px = centerX * _scale;
            var py = centerY * _scale;
            var pr = Math.Max(0.8, radius * _scale);
            var reach = pr * pr;
            var left = (int)Math.Floor(px - pr);
            var top = (int)Math.Floor(py - pr);
            var right = (int)Math.Ceiling(px + pr);
            var bottom = (int)Math.Ceiling(py + pr);
            for (var y = top; y <= bottom; y++)
            {
                for (var x = left; x <= right; x++)
                {
                    var dx = x + 0.5 - px;
                    var dy = y + 0.5 - py;
                    if (dx * dx + dy * dy <= reach)
                        Blend(x, y, color);
                }
            }
        }

        public void Present(WriteableBitmap bitmap)
        {
            using var stream = bitmap.PixelBuffer.AsStream();
            stream.Position = 0;
            stream.Write(_pixels, 0, _pixels.Length);
            bitmap.Invalidate();
        }

        void EnsureRow(int width, Color color)
        {
            var bytes = width * 4;
            if (_row.Length < bytes)
                _row = new byte[Math.Max(bytes, 4096)];
            for (var i = 0; i < width; i++)
            {
                _row[i * 4] = color.B;
                _row[i * 4 + 1] = color.G;
                _row[i * 4 + 2] = color.R;
                _row[i * 4 + 3] = 255;
            }
        }

        void Blend(int x, int y, Color color)
        {
            if ((uint)x >= (uint)_width || (uint)y >= (uint)_height || color.A == 0)
                return;
            var index = (y * _width + x) * 4;
            if (color.A == 255)
            {
                _pixels[index] = color.B;
                _pixels[index + 1] = color.G;
                _pixels[index + 2] = color.R;
                _pixels[index + 3] = 255;
                return;
            }

            var amount = color.A / 255.0;
            var keep = 1 - amount;
            _pixels[index] = (byte)(color.B * amount + _pixels[index] * keep);
            _pixels[index + 1] = (byte)(color.G * amount + _pixels[index + 1] * keep);
            _pixels[index + 2] = (byte)(color.R * amount + _pixels[index + 2] * keep);
            _pixels[index + 3] = 255;
        }
    }
}
