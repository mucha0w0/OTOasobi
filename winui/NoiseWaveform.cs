using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WavePath = Microsoft.UI.Xaml.Shapes.Path;
using Windows.Foundation;

namespace Otoasobi;

sealed class NoiseRangeEventArgs : EventArgs
{
    public required double Start { get; init; }

    public required double End { get; init; }
}

// エフェクターのカードに置く、拡大できる波形。ドラッグした範囲を無音として返す。
sealed class NoiseWaveform : UserControl
{
    const double PlotHeight = 88;
    const double QuietWindowSeconds = 0.8;
    const double OpenViewSeconds = 2;
    const double ResizePreviewOpacity = 0.55;

    readonly Border _frame = new()
    {
        Height = PlotHeight,
        CornerRadius = new CornerRadius(4),
    };
    readonly Canvas _canvas = new() { Height = PlotHeight };
    readonly WavePath _wave = new() { StrokeThickness = 1, IsHitTestVisible = false };
    readonly Rectangle _shade = new() { IsHitTestVisible = false, Opacity = 0.32 };
    readonly Rectangle _zero = new() { Height = 1, IsHitTestVisible = false, Opacity = 0.45 };
    readonly ScrollBar _bar = new()
    {
        Orientation = Orientation.Horizontal,
        IndicatorMode = ScrollingIndicatorMode.MouseIndicator,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Height = 18,
        MinHeight = 18,
        Margin = new Thickness(0, 4, 0, 0),
        ViewportSize = 1,
    };

    float[] _mono = [];
    int _rate;
    double _viewStart;
    double _viewLength = 1;
    double? _rangeStart;
    double? _rangeEnd;
    bool _dragging;
    double _dragAnchor;
    bool _barSync;
    bool _columnDrag;
    bool _resizePreview;
    double _columnDragWidth = 1;
    double _opacityBeforePreview = 1;
    ScaleTransform? _columnDragScale;
    FrameworkElement? _columnDragRoot;

    public NoiseWaveform()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _canvas.Children.Add(_zero);
        _canvas.Children.Add(_shade);
        _canvas.Children.Add(_wave);
        _frame.Child = _canvas;
        _frame.PointerPressed += Plot_Pressed;
        _frame.PointerMoved += Plot_Moved;
        _frame.PointerReleased += Plot_Released;
        _frame.PointerCanceled += Plot_Released;
        ToolTipService.SetToolTip(_frame, "ドラッグで無音の範囲を選びます。ホイールで拡大し、下のバーで移動します。");
        Grid.SetRow(_bar, 1);
        grid.Children.Add(_frame);
        grid.Children.Add(_bar);
        _bar.ValueChanged += Bar_Changed;
        Content = grid;
        _columnDragRoot = grid;
        ActualThemeChanged += (_, _) => ApplyTheme();
        Loaded += (_, _) => ApplyTheme();
        ApplyTheme();
        SizeChanged += (_, e) =>
        {
            if (_columnDrag)
            {
                if (_columnDragScale != null && _columnDragWidth > 1)
                    _columnDragScale.ScaleX = e.NewSize.Width / _columnDragWidth;
                return;
            }

            Draw();
        };
    }

    public void SetColumnDrag(bool dragging)
    {
        if (_columnDragRoot == null)
            return;
        if (dragging)
        {
            if (_columnDrag || ActualWidth < 2)
                return;
            _columnDragWidth = ActualWidth;
            _columnDrag = true;
            _columnDragScale ??= new ScaleTransform();
            _columnDragScale.ScaleX = 1;
            _columnDragRoot.HorizontalAlignment = HorizontalAlignment.Left;
            _columnDragRoot.Width = _columnDragWidth;
            _columnDragRoot.RenderTransformOrigin = new Point(0, 0);
            _columnDragRoot.RenderTransform = _columnDragScale;
            BeginResizePreview();
            return;
        }

        if (!_columnDrag)
            return;
        _columnDrag = false;
        _columnDragRoot.RenderTransform = null;
        _columnDragRoot.ClearValue(WidthProperty);
        _columnDragRoot.HorizontalAlignment = HorizontalAlignment.Stretch;
        EndResizePreview();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!_columnDrag)
                Draw();
        });
    }

    void BeginResizePreview()
    {
        if (_resizePreview)
            return;
        _resizePreview = true;
        _opacityBeforePreview = Opacity;
        Opacity = ResizePreviewOpacity;
    }

    void EndResizePreview()
    {
        if (!_resizePreview)
            return;
        _resizePreview = false;
        Opacity = _opacityBeforePreview;
    }

    public event EventHandler<NoiseRangeEventArgs>? RangeChanging;

    public event EventHandler<NoiseRangeEventArgs>? RangeChosen;

    public void Clear()
    {
        _mono = [];
        _rate = 0;
        _rangeStart = null;
        _rangeEnd = null;
        _dragging = false;
        Draw();
    }

    public void SetAudio(float[] mono, int sampleRate)
    {
        _mono = mono;
        _rate = sampleRate;
        _rangeStart = null;
        _rangeEnd = null;
        _dragging = false;
        FocusQuiet();
        Draw();
    }

    public void SetRange(double start, double end)
    {
        if (end < start)
            (start, end) = (end, start);
        _rangeStart = start;
        _rangeEnd = end;
        _dragging = false;
        Draw();
    }

    public void ClearRange()
    {
        _rangeStart = null;
        _rangeEnd = null;
        _dragging = false;
        Draw();
    }

    protected override void OnPointerWheelChanged(PointerRoutedEventArgs e)
    {
        if (_mono.Length == 0 || _rate <= 0)
            return;
        var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        if (delta == 0)
            return;
        var x = e.GetCurrentPoint(_frame).Position.X;
        Zoom(TimeAt(x), delta > 0 ? 1 / 1.25 : 1.25);
        e.Handled = true;
    }

    void Plot_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (_mono.Length == 0 || _rate <= 0)
            return;
        var point = e.GetCurrentPoint(_frame);
        if (!point.Properties.IsLeftButtonPressed)
            return;
        _dragging = true;
        _dragAnchor = TimeAt(point.Position.X);
        _rangeStart = _dragAnchor;
        _rangeEnd = _dragAnchor;
        _frame.CapturePointer(e.Pointer);
        Draw();
        e.Handled = true;
    }

    void Plot_Moved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
            return;
        _rangeEnd = TimeAt(e.GetCurrentPoint(_frame).Position.X);
        Draw();
        RangeChanging?.Invoke(this, new NoiseRangeEventArgs { Start = RangeStart(), End = RangeEnd() });
        e.Handled = true;
    }

    void Plot_Released(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
            return;
        _dragging = false;
        _rangeEnd = TimeAt(e.GetCurrentPoint(_frame).Position.X);
        if (_frame.PointerCaptures.Any(captured => captured.PointerId == e.Pointer.PointerId))
            _frame.ReleasePointerCapture(e.Pointer);
        Draw();
        RangeChosen?.Invoke(this, new NoiseRangeEventArgs { Start = RangeStart(), End = RangeEnd() });
        e.Handled = true;
    }

    void Bar_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_barSync)
            return;
        _viewStart = e.NewValue;
        Draw();
    }

    void FocusQuiet()
    {
        var duration = Duration();
        if (duration <= 0)
        {
            _viewStart = 0;
            _viewLength = 1;
            return;
        }

        _viewLength = Math.Min(duration, OpenViewSeconds);
        var frame = Math.Max(1, _rate / 50);
        var frames = _mono.Length / frame;
        if (frames < 2)
        {
            _viewStart = 0;
            ClampView();
            return;
        }

        var energy = new double[frames];
        for (var i = 0; i < frames; i++)
        {
            double sum = 0;
            var offset = i * frame;
            for (var n = 0; n < frame; n++)
            {
                var sample = _mono[offset + n];
                sum += sample * (double)sample;
            }

            energy[i] = sum / frame;
        }

        var window = Math.Clamp((int)Math.Round(QuietWindowSeconds * _rate / frame), 1, frames);
        double running = 0;
        for (var i = 0; i < window; i++)
            running += energy[i];
        var best = 0;
        var bestMean = running / window;
        for (var i = window; i < frames; i++)
        {
            running += energy[i] - energy[i - window];
            var mean = running / window;
            if (mean < bestMean)
            {
                bestMean = mean;
                best = i - window + 1;
            }
        }

        var center = (best + window * 0.5) * frame / _rate;
        _viewStart = center - _viewLength / 2;
        ClampView();
    }

    void Zoom(double center, double factor)
    {
        var duration = Duration();
        if (duration <= 0)
            return;
        var next = Math.Clamp(_viewLength * factor, Math.Min(duration, 0.12), duration);
        var anchor = _viewLength <= 0 ? 0 : (center - _viewStart) / _viewLength;
        _viewLength = next;
        _viewStart = center - anchor * next;
        ClampView();
        Draw();
    }

    void ClampView()
    {
        var duration = Duration();
        _viewLength = Math.Clamp(_viewLength, Math.Min(duration, 0.12), Math.Max(duration, 0.12));
        if (_viewLength >= duration)
        {
            _viewStart = 0;
            _viewLength = duration;
            return;
        }

        _viewStart = Math.Clamp(_viewStart, 0, duration - _viewLength);
    }

    void Draw()
    {
        var width = _frame.ActualWidth;
        if (width < 2 || _mono.Length == 0 || _rate <= 0)
        {
            _wave.Data = null;
            _shade.Width = 0;
            SyncBar();
            return;
        }

        var columns = Math.Max(1, (int)Math.Floor(width));
        var figure = new PathFigure { IsClosed = true };
        var top = new Point[columns];
        var bottom = new Point[columns];
        var mid = PlotHeight / 2;
        for (var column = 0; column < columns; column++)
        {
            var t0 = _viewStart + _viewLength * column / columns;
            var t1 = _viewStart + _viewLength * (column + 1) / columns;
            Peaks(t0, t1, out var min, out var max);
            var x = column + 0.5;
            top[column] = new Point(x, mid - max * (mid - 2));
            bottom[column] = new Point(x, mid - min * (mid - 2));
        }

        if (columns > 0)
        {
            figure.StartPoint = top[0];
            for (var column = 1; column < columns; column++)
                figure.Segments.Add(new LineSegment { Point = top[column] });
            for (var column = columns - 1; column >= 0; column--)
                figure.Segments.Add(new LineSegment { Point = bottom[column] });
        }

        _wave.Data = new PathGeometry { Figures = { figure } };
        ApplyTheme();
        Canvas.SetLeft(_zero, 0);
        Canvas.SetTop(_zero, mid);
        _zero.Width = width;
        PlaceShade(width);
        SyncBar();
    }

    void PlaceShade(double width)
    {
        if (_rangeStart is not double start || _rangeEnd is not double end)
        {
            _shade.Width = 0;
            return;
        }

        if (end < start)
            (start, end) = (end, start);
        var viewEnd = _viewStart + _viewLength;
        var left = Math.Max(start, _viewStart);
        var right = Math.Min(end, viewEnd);
        if (right <= left || _viewLength <= 0)
        {
            _shade.Width = 0;
            return;
        }

        var x0 = (left - _viewStart) / _viewLength * width;
        var x1 = (right - _viewStart) / _viewLength * width;
        Canvas.SetLeft(_shade, x0);
        Canvas.SetTop(_shade, 0);
        _shade.Width = Math.Max(1, x1 - x0);
        _shade.Height = PlotHeight;
    }

    void Peaks(double t0, double t1, out float min, out float max)
    {
        var i0 = Math.Clamp((int)Math.Floor(t0 * _rate), 0, _mono.Length - 1);
        var i1 = Math.Clamp((int)Math.Ceiling(t1 * _rate), i0 + 1, _mono.Length);
        min = _mono[i0];
        max = min;
        for (var i = i0; i < i1; i++)
        {
            var sample = _mono[i];
            if (sample < min)
                min = sample;
            if (sample > max)
                max = sample;
        }

        min = Math.Clamp(min, -1f, 1f);
        max = Math.Clamp(max, -1f, 1f);
    }

    void SyncBar()
    {
        var duration = Duration();
        var span = Math.Max(0, duration - _viewLength);
        _barSync = true;
        _bar.Value = 0;
        _bar.Minimum = 0;
        _bar.ViewportSize = 0;
        _bar.Maximum = span <= 0 ? 0 : span;
        _bar.ViewportSize = span <= 0 ? 0 : _viewLength;
        _bar.SmallChange = Math.Max(_viewLength * 0.1, 0.01);
        _bar.LargeChange = Math.Max(_viewLength * 0.5, 0.05);
        _bar.Value = span <= 0 ? 0 : Math.Clamp(_viewStart, 0, span);
        _bar.IsEnabled = span > 0.001;
        _barSync = false;
    }

    double TimeAt(double x)
    {
        var width = Math.Max(_frame.ActualWidth, 1);
        return Math.Clamp(_viewStart + x / width * _viewLength, 0, Duration());
    }

    double Duration() => _rate <= 0 ? 0 : _mono.Length / (double)_rate;

    double RangeStart() => Math.Min(_rangeStart ?? 0, _rangeEnd ?? 0);

    double RangeEnd() => Math.Max(_rangeStart ?? 0, _rangeEnd ?? 0);

    void ApplyTheme()
    {
        var theme = AppearanceTheme.Resolve(this);
        _frame.Background = AppearanceTheme.Brush(theme, "ControlFillColorSecondaryBrush") ?? _frame.Background;
        var ink = AppearanceTheme.Brush(theme, "TextFillColorPrimaryBrush");
        if (ink != null)
        {
            _wave.Fill = ink;
            _wave.Stroke = ink;
        }

        _zero.Fill = AppearanceTheme.Brush(theme, "TextFillColorSecondaryBrush") ?? _zero.Fill;
        _shade.Fill = AppearanceTheme.Brush(theme, "AccentFillColorDefaultBrush") ?? _shade.Fill;
    }
}
