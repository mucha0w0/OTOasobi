using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using WavePath = Microsoft.UI.Xaml.Shapes.Path;

namespace Otoasobi;

// マイクモニターの波形。サンプル全体か、山の列を幅に合わせて描く。
sealed class RecTrace : UserControl
{
    const double ResizePreviewOpacity = 0.55;

    readonly Border _frame = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        CornerRadius = new CornerRadius(4),
    };
    readonly Canvas _canvas = new();
    readonly WavePath _wave = new() { IsHitTestVisible = false };
    readonly Rectangle _zero = new() { Height = 1, IsHitTestVisible = false, Opacity = 0.45 };
    readonly Rectangle _playhead = new() { Width = 2, IsHitTestVisible = false, Visibility = Visibility.Collapsed };

    float[] _live = [];
    float[] _min = [];
    float[] _max = [];
    int _rate = 1;
    int _sampleCount;
    bool _buckets;
    double? _playheadSeconds;
    bool _columnDrag;
    bool _resizePreview;
    double _columnDragWidth = 1;
    double _opacityBeforePreview = 1;
    ScaleTransform? _columnDragScale;
    FrameworkElement? _columnDragRoot;

    public RecTrace()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        _canvas.Children.Add(_zero);
        _canvas.Children.Add(_wave);
        _canvas.Children.Add(_playhead);
        _frame.Child = _canvas;
        var grid = new Grid();
        grid.Children.Add(_frame);
        Content = grid;
        _columnDragRoot = grid;
        ActualThemeChanged += (_, _) => ApplyTheme();
        Loaded += (_, _) => ApplyTheme();
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
        ApplyTheme();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var desired = base.MeasureOverride(availableSize);
        var width = double.IsInfinity(availableSize.Width) ? desired.Width : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? desired.Height : availableSize.Height;
        return new Size(Math.Max(0, width), Math.Max(0, height));
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
            if (!_resizePreview)
            {
                _resizePreview = true;
                _opacityBeforePreview = Opacity;
                Opacity = ResizePreviewOpacity;
            }

            return;
        }

        if (!_columnDrag)
            return;
        _columnDrag = false;
        _columnDragRoot.RenderTransform = null;
        _columnDragRoot.ClearValue(WidthProperty);
        _columnDragRoot.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (_resizePreview)
        {
            _resizePreview = false;
            Opacity = _opacityBeforePreview;
        }

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!_columnDrag)
                Draw();
        });
    }

    public void Clear()
    {
        _live = [];
        _min = [];
        _max = [];
        _sampleCount = 0;
        _buckets = false;
        _playheadSeconds = null;
        Draw();
    }

    public void SetLive(float[] mono, int sampleRate)
    {
        _live = mono;
        _rate = Math.Max(1, sampleRate);
        _sampleCount = mono.Length;
        _buckets = false;
        Draw();
    }

    public void SetBuckets(float[] min, float[] max, int sampleCount, int sampleRate)
    {
        _min = min;
        _max = max;
        _sampleCount = Math.Max(0, sampleCount);
        _rate = Math.Max(1, sampleRate);
        _buckets = true;
        _live = [];
        Draw();
    }

    public void SetPlayhead(double? seconds)
    {
        _playheadSeconds = seconds;
        PlacePlayhead();
    }

    void ApplyTheme()
    {
        var theme = AppearanceTheme.Resolve(this);
        _frame.Background = AppearanceTheme.Brush(theme, "SolidBackgroundFillColorBaseBrush") ?? _frame.Background;
        var ink = AppearanceTheme.Brush(theme, "TextFillColorPrimaryBrush");
        _wave.Fill = ink;
        _wave.Stroke = ink;
        _wave.StrokeThickness = 1;
        _zero.Fill = AppearanceTheme.Brush(theme, "TextFillColorSecondaryBrush") ?? _zero.Fill;
        _playhead.Fill = AppearanceTheme.Brush(theme, "AccentFillColorDefaultBrush") ?? _playhead.Fill;
    }

    void Draw()
    {
        if (_columnDrag)
            return;
        var width = ActualWidth;
        var height = ActualHeight;
        _canvas.Width = width;
        _canvas.Height = height;
        if (width < 2 || height < 2 || _sampleCount <= 0 || (_buckets ? _min.Length == 0 : _live.Length == 0))
        {
            _wave.Data = null;
            _playhead.Visibility = Visibility.Collapsed;
            _zero.Width = Math.Max(0, width);
            Canvas.SetTop(_zero, height / 2);
            return;
        }

        var columns = Math.Max(1, (int)Math.Floor(width));
        var figure = new PathFigure { IsClosed = true };
        var top = new Point[columns];
        var bottom = new Point[columns];
        var mid = height / 2;
        var reach = Math.Max(1, mid - 2);
        for (var column = 0; column < columns; column++)
        {
            Range(column, columns, out var min, out var max);
            var x = column + 0.5;
            top[column] = new Point(x, mid - max * reach);
            bottom[column] = new Point(x, mid - min * reach);
        }

        figure.StartPoint = top[0];
        for (var column = 1; column < columns; column++)
            figure.Segments.Add(new LineSegment { Point = top[column] });
        for (var column = columns - 1; column >= 0; column--)
            figure.Segments.Add(new LineSegment { Point = bottom[column] });
        _wave.Data = new PathGeometry { Figures = { figure } };
        Canvas.SetLeft(_zero, 0);
        Canvas.SetTop(_zero, mid);
        _zero.Width = width;
        PlacePlayhead();
    }

    void Range(int column, int columns, out float min, out float max)
    {
        if (_buckets)
        {
            var start = (int)((long)column * _min.Length / columns);
            var end = (int)((long)(column + 1) * _min.Length / columns);
            if (end <= start)
                end = Math.Min(_min.Length, start + 1);
            min = _min[start];
            max = _max[start];
            for (var i = start; i < end; i++)
            {
                if (_min[i] < min)
                    min = _min[i];
                if (_max[i] > max)
                    max = _max[i];
            }

            return;
        }

        var i0 = (int)((long)column * _live.Length / columns);
        var i1 = (int)((long)(column + 1) * _live.Length / columns);
        if (i1 <= i0)
            i1 = Math.Min(_live.Length, i0 + 1);
        min = 0;
        max = 0;
        for (var i = i0; i < i1; i++)
        {
            var sample = Math.Clamp(_live[i], -1f, 1f);
            if (sample < min)
                min = sample;
            if (sample > max)
                max = sample;
        }
    }

    void PlacePlayhead()
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (_playheadSeconds is not double seconds || _sampleCount <= 0 || _rate <= 0 || width < 2 || height < 2)
        {
            _playhead.Visibility = Visibility.Collapsed;
            return;
        }

        var duration = _sampleCount / (double)_rate;
        if (duration <= 0)
        {
            _playhead.Visibility = Visibility.Collapsed;
            return;
        }

        var x = Math.Clamp(seconds / duration, 0, 1) * width;
        Canvas.SetLeft(_playhead, x);
        Canvas.SetTop(_playhead, 0);
        _playhead.Height = height;
        _playhead.Visibility = Visibility.Visible;
    }
}
