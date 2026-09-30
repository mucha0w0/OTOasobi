using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace Otoasobi;

public sealed class EqCurve : UserControl
{
    const double FMin = 40;
    const double FMax = 16000;
    const double GutterLeft = 34;
    const double GutterRight = 12;
    const double GutterTop = 10;
    const double GutterBottom = 22;

    static readonly double[] GridDb = [12, 6, 0, -6, -12];
    static readonly Color CurveColor = Color.FromArgb(255, 72, 210, 214);

    readonly Canvas _canvas = new() { Background = Brush(255, 20, 20, 20) };
    readonly double[] _gains = new double[EqualizerEffect.BandCount];
    readonly Line[] _hLines = new Line[GridDb.Length];
    readonly Line[] _vLines = new Line[EqualizerEffect.BandCount];
    readonly Ellipse[] _halos = new Ellipse[EqualizerEffect.BandCount];
    readonly Ellipse[] _handles = new Ellipse[EqualizerEffect.BandCount];
    readonly TextBlock[] _freqLabels = new TextBlock[EqualizerEffect.BandCount];
    readonly TextBlock[] _dbLabels = new TextBlock[3];
    readonly Polygon _fill = new() { IsHitTestVisible = false };
    readonly Polyline _line = new()
    {
        StrokeThickness = 2.25,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        IsHitTestVisible = false,
    };
    readonly SolidColorBrush _curve = new(CurveColor);
    readonly SolidColorBrush _halo = new(Color.FromArgb(72, CurveColor.R, CurveColor.G, CurveColor.B));
    readonly SolidColorBrush _knob = Brush(255, 232, 252, 252);
    readonly SolidColorBrush _label = Brush(180, 150, 150, 150);
    readonly SolidColorBrush _labelHot = Brush(255, 230, 244, 244);
    readonly InputCursor _arrow = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    readonly InputCursor _ns = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);

    double _plotLeft;
    double _plotTop;
    double _plotWidth;
    double _plotHeight;
    int _active = -1;
    int _drag = -1;
    int _lastBand = -1;
    long _lastPressMs;
    double _pressY;
    bool _moved;
    bool _click;
    bool _captured;

    public EqCurve()
    {
        _fill.Fill = new SolidColorBrush(Color.FromArgb(36, CurveColor.R, CurveColor.G, CurveColor.B));
        _line.Stroke = _curve;
        Build();
        var root = new Grid { Background = Brush(255, 20, 20, 20) };
        root.Children.Add(_canvas);
        Content = root;
        Background = Brush(255, 20, 20, 20);
        ManipulationMode = ManipulationModes.None;
        IsTabStop = true;
        UseLayoutRounding = true;
        AutomationProperties.SetName(this, "イコライザーのカーブ");
        AutomationProperties.SetHelpText(this, "点を上下にドラッグして、その周波数の大きさを変えます。");
        AddHandler(PointerPressedEvent, new PointerEventHandler(OnPressed), true);
        AddHandler(PointerMovedEvent, new PointerEventHandler(OnMoved), true);
        AddHandler(PointerReleasedEvent, new PointerEventHandler(OnReleased), true);
        AddHandler(PointerCanceledEvent, new PointerEventHandler(OnReleased), true);
        PointerCaptureLost += OnCaptureLost;
        PointerExited += OnExited;
        KeyDown += OnKey;
        SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) => Redraw();
    }

    public IReadOnlyList<double> Gains => _gains;

    public int ActiveBand => _active;

    public bool IsDragging => _drag >= 0;

    public event EventHandler? Edited;

    public event EventHandler? EditCompleted;

    public event EventHandler? InteractionStarted;

    public void SetGains(IReadOnlyList<double> gains)
    {
        for (var i = 0; i < _gains.Length; i++)
        {
            _gains[i] = i < gains.Count
                ? EqualizerEffect.QuantizeGain(gains[i], magnet: false)
                : 0;
        }

        Redraw();
    }

    void Build()
    {
        var grid = Brush(32, 255, 255, 255);
        var zero = Brush(90, 255, 255, 255);
        for (var i = 0; i < _vLines.Length; i++)
        {
            var line = new Line
            {
                Stroke = grid,
                StrokeThickness = 1,
                IsHitTestVisible = false,
            };
            _vLines[i] = line;
            _canvas.Children.Add(line);
        }

        for (var i = 0; i < GridDb.Length; i++)
        {
            var db = GridDb[i];
            var line = new Line
            {
                Stroke = db == 0 ? zero : grid,
                StrokeThickness = 1,
                IsHitTestVisible = false,
            };
            _hLines[i] = line;
            _canvas.Children.Add(line);
        }

        _canvas.Children.Add(_fill);
        _canvas.Children.Add(_line);
        for (var i = 0; i < _halos.Length; i++)
        {
            var halo = new Ellipse
            {
                Fill = _halo,
                IsHitTestVisible = false,
            };
            _halos[i] = halo;
            _canvas.Children.Add(halo);
            var handle = new Ellipse
            {
                Fill = _knob,
                Stroke = _curve,
                StrokeThickness = 2,
                IsHitTestVisible = false,
            };
            _handles[i] = handle;
            _canvas.Children.Add(handle);
        }

        for (var i = 0; i < _freqLabels.Length; i++)
        {
            var label = new TextBlock
            {
                Text = EqualizerEffect.FormatTick(EqualizerEffect.Frequencies[i]),
                FontSize = 10,
                Foreground = _label,
                IsHitTestVisible = false,
            };
            _freqLabels[i] = label;
            _canvas.Children.Add(label);
        }

        string[] captions = ["+12", "0", "−12"];
        for (var i = 0; i < _dbLabels.Length; i++)
        {
            var label = new TextBlock
            {
                Text = captions[i],
                FontSize = 10,
                Foreground = _label,
                IsHitTestVisible = false,
            };
            _dbLabels[i] = label;
            _canvas.Children.Add(label);
        }
    }

    void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
            return;
        if (_plotWidth < 16)
            Redraw();
        var index = BandAt(point.Position);
        if (index < 0)
            return;
        Focus(FocusState.Pointer);
        var now = Environment.TickCount64;
        var repeat = _click && index == _lastBand && now - _lastPressMs < 400;
        _lastPressMs = now;
        _lastBand = index;
        _click = false;
        _moved = false;
        _pressY = point.Position.Y;
        if (repeat)
        {
            _drag = -1;
            _gains[index] = 0;
            _active = index;
            Redraw();
            Edited?.Invoke(this, EventArgs.Empty);
            EditCompleted?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        _active = index;
        _drag = index;
        ProtectedCursor = _ns;
        CancelDirectManipulations();
        InteractionStarted?.Invoke(this, EventArgs.Empty);
        _captured = CapturePointer(e.Pointer);
        ApplyGain(index, point.Position.Y);
        e.Handled = true;
    }

    void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this).Position;
        if (_drag >= 0)
        {
            if (Math.Abs(point.Y - _pressY) > 3)
                _moved = true;
            ApplyGain(_drag, point.Y);
            e.Handled = true;
            return;
        }

        var cursor = BandAt(point) >= 0 ? _ns : _arrow;
        if (!ReferenceEquals(ProtectedCursor, cursor))
            ProtectedCursor = cursor;
    }

    void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_drag < 0)
            return;
        _drag = -1;
        _click = !_moved;
        if (_captured)
        {
            _captured = false;
            ReleasePointerCapture(e.Pointer);
        }

        EditCompleted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    void OnCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_drag < 0)
            return;
        _drag = -1;
        _captured = false;
        _click = !_moved;
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    void OnExited(object sender, PointerRoutedEventArgs e)
    {
        if (_drag >= 0 && !_captured)
        {
            _drag = -1;
            _click = !_moved;
            EditCompleted?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_drag < 0 && !ReferenceEquals(ProtectedCursor, _arrow))
            ProtectedCursor = _arrow;
    }

    void OnKey(object sender, KeyRoutedEventArgs e)
    {
        var key = e.Key;
        if (key is not (VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or VirtualKey.Delete or VirtualKey.Back))
            return;
        var band = _active < 0 ? 4 : _active;
        if (key == VirtualKey.Left)
            band = Math.Max(0, _active < 0 ? band : band - 1);
        else if (key == VirtualKey.Right)
            band = Math.Min(EqualizerEffect.BandCount - 1, _active < 0 ? band : band + 1);
        else if (key == VirtualKey.Up)
            _gains[band] = EqualizerEffect.QuantizeGain(_gains[band] + 0.5, magnet: false);
        else if (key == VirtualKey.Down)
            _gains[band] = EqualizerEffect.QuantizeGain(_gains[band] - 0.5, magnet: false);
        else
            _gains[band] = 0;
        _active = band;
        _click = false;
        Redraw();
        Edited?.Invoke(this, EventArgs.Empty);
        EditCompleted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    void ApplyGain(int band, double y)
    {
        if (_plotHeight < 16)
            return;
        var gain = EqualizerEffect.QuantizeGain(DbOfY(y), magnet: true);
        var changed = gain != _gains[band] || _active != band;
        _gains[band] = gain;
        _active = band;
        if (!changed)
            return;
        Redraw();
        Edited?.Invoke(this, EventArgs.Empty);
    }

    int BandAt(Point point)
    {
        if (_plotWidth < 16 || _plotHeight < 16)
            return -1;
        var best = -1;
        var bestDist = 26.0;
        for (var i = 0; i < EqualizerEffect.BandCount; i++)
        {
            var dx = point.X - XOfFreq(EqualizerEffect.Frequencies[i]);
            var dy = point.Y - YOfDb(_gains[i]);
            var dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist >= bestDist)
                continue;
            bestDist = dist;
            best = i;
        }

        if (best >= 0)
            return best;
        if (point.Y < _plotTop - 18 || point.Y > _plotTop + _plotHeight + 8)
            return -1;
        if (point.X < _plotLeft - 6 || point.X > _plotLeft + _plotWidth + 6)
            return -1;
        bestDist = double.MaxValue;
        for (var i = 0; i < EqualizerEffect.BandCount; i++)
        {
            var dx = Math.Abs(point.X - XOfFreq(EqualizerEffect.Frequencies[i]));
            if (dx >= bestDist)
                continue;
            bestDist = dx;
            best = i;
        }

        return best;
    }

    void Redraw()
    {
        var width = ActualWidth;
        var height = ActualHeight;
        _plotLeft = GutterLeft;
        _plotTop = GutterTop;
        _plotWidth = width - GutterLeft - GutterRight;
        _plotHeight = height - GutterTop - GutterBottom;
        if (_plotWidth < 16 || _plotHeight < 16)
            return;

        var plotRight = _plotLeft + _plotWidth;
        var plotBottom = _plotTop + _plotHeight;
        for (var i = 0; i < _vLines.Length; i++)
        {
            var x = XOfFreq(EqualizerEffect.Frequencies[i]);
            _vLines[i].X1 = x;
            _vLines[i].X2 = x;
            _vLines[i].Y1 = _plotTop;
            _vLines[i].Y2 = plotBottom;
        }

        for (var i = 0; i < GridDb.Length; i++)
        {
            var y = YOfDb(GridDb[i]);
            _hLines[i].X1 = _plotLeft;
            _hLines[i].X2 = plotRight;
            _hLines[i].Y1 = y;
            _hLines[i].Y2 = y;
        }

        var line = new PointCollection();
        AddSmooth(line);
        var fill = new PointCollection();
        foreach (var point in line)
            fill.Add(point);
        var zeroY = YOfDb(0);
        fill.Add(new Point(plotRight, zeroY));
        fill.Add(new Point(_plotLeft, zeroY));
        _line.Points = line;
        _fill.Points = fill;

        for (var i = 0; i < EqualizerEffect.BandCount; i++)
        {
            var x = XOfFreq(EqualizerEffect.Frequencies[i]);
            var y = YOfDb(_gains[i]);
            var hot = i == _active;
            var haloSize = hot ? 22.0 : 16.0;
            var halo = _halos[i];
            halo.Width = haloSize;
            halo.Height = haloSize;
            Canvas.SetLeft(halo, x - haloSize / 2);
            Canvas.SetTop(halo, y - haloSize / 2);
            var size = hot ? 12.0 : 10.0;
            var handle = _handles[i];
            handle.Width = size;
            handle.Height = size;
            handle.Fill = hot ? _curve : _knob;
            handle.Stroke = hot ? _knob : _curve;
            Canvas.SetLeft(handle, x - size / 2);
            Canvas.SetTop(handle, y - size / 2);
            var caption = _freqLabels[i];
            caption.Foreground = hot ? _labelHot : _label;
            Place(caption, x, plotBottom + 11, center: true, width);
        }

        Place(_dbLabels[0], 2, YOfDb(12), center: false, width);
        Place(_dbLabels[1], 2, YOfDb(0), center: false, width);
        Place(_dbLabels[2], 2, YOfDb(-12), center: false, width);
    }

    // 点を通り、区間の中で行き過ぎない三次曲線。
    void AddSmooth(PointCollection dest)
    {
        var count = EqualizerEffect.BandCount + 2;
        var xs = new double[count];
        var ys = new double[count];
        xs[0] = _plotLeft;
        ys[0] = YOfDb(0);
        for (var i = 0; i < EqualizerEffect.BandCount; i++)
        {
            xs[i + 1] = XOfFreq(EqualizerEffect.Frequencies[i]);
            ys[i + 1] = YOfDb(_gains[i]);
        }

        xs[^1] = _plotLeft + _plotWidth;
        ys[^1] = YOfDb(0);
        for (var i = 1; i < count; i++)
        {
            if (xs[i] <= xs[i - 1])
                xs[i] = xs[i - 1] + 0.5;
        }

        var slope = new double[count - 1];
        for (var i = 0; i < slope.Length; i++)
            slope[i] = (ys[i + 1] - ys[i]) / (xs[i + 1] - xs[i]);

        var tangent = new double[count];
        tangent[0] = slope[0];
        tangent[^1] = slope[^1];
        for (var i = 1; i < count - 1; i++)
        {
            if (slope[i - 1] == 0 || slope[i] == 0 || slope[i - 1] * slope[i] < 0)
                tangent[i] = 0;
            else
                tangent[i] = (slope[i - 1] + slope[i]) * 0.5;
        }

        for (var i = 0; i < slope.Length; i++)
        {
            if (slope[i] == 0)
            {
                tangent[i] = 0;
                tangent[i + 1] = 0;
                continue;
            }

            var a = tangent[i] / slope[i];
            var b = tangent[i + 1] / slope[i];
            var sum = a * a + b * b;
            if (sum <= 9)
                continue;
            var scale = 3 / Math.Sqrt(sum);
            tangent[i] = scale * a * slope[i];
            tangent[i + 1] = scale * b * slope[i];
        }

        var samples = Math.Max(2, (int)Math.Round(_plotWidth));
        var span = count - 1;
        var interval = 0;
        for (var step = 0; step <= samples; step++)
        {
            var x = _plotLeft + _plotWidth * step / samples;
            while (interval < span - 1 && x > xs[interval + 1])
                interval++;
            var width = xs[interval + 1] - xs[interval];
            var t = width < 1e-6 ? 0 : (x - xs[interval]) / width;
            var t2 = t * t;
            var t3 = t2 * t;
            var y = (2 * t3 - 3 * t2 + 1) * ys[interval]
                + (t3 - 2 * t2 + t) * width * tangent[interval]
                + (-2 * t3 + 3 * t2) * ys[interval + 1]
                + (t3 - t2) * width * tangent[interval + 1];
            dest.Add(new Point(x, y));
        }
    }

    double XOfFreq(double freq)
    {
        var t = Math.Log(freq / FMin) / Math.Log(FMax / FMin);
        return _plotLeft + Math.Clamp(t, 0, 1) * _plotWidth;
    }

    double YOfDb(double db)
    {
        db = Math.Clamp(db, EqualizerEffect.MinDb, EqualizerEffect.MaxDb);
        var span = EqualizerEffect.MaxDb - EqualizerEffect.MinDb;
        var t = (EqualizerEffect.MaxDb - db) / span;
        return _plotTop + t * _plotHeight;
    }

    double DbOfY(double y)
    {
        var t = Math.Clamp((y - _plotTop) / _plotHeight, 0, 1);
        return EqualizerEffect.MaxDb - t * (EqualizerEffect.MaxDb - EqualizerEffect.MinDb);
    }

    static void Place(TextBlock block, double x, double y, bool center, double width)
    {
        block.Measure(new Size(80, 20));
        var w = block.DesiredSize.Width;
        var h = block.DesiredSize.Height;
        var left = center ? x - w / 2 : x;
        left = Math.Clamp(left, 0, Math.Max(0, width - w));
        Canvas.SetLeft(block, left);
        Canvas.SetTop(block, y - h / 2);
    }

    static SolidColorBrush Brush(byte a, byte r, byte g, byte b) =>
        new(Color.FromArgb(a, r, g, b));
}
