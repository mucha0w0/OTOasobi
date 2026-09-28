using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Otoasobi;

public sealed class SpanEditor : UserControl
{
    const double Pad = 10;
    const double Ruler = 18;
    const double MinSpan = 0.005;
    const double LinkGap = 0.0005;
    const double HitPx = 18;
    const double LoudnessRangeDb = 40;

    enum DragKind
    {
        None,
        Boundary,
        Pan,
    }

    readonly Canvas _plot = new();
    readonly TextBlock _readout = new();
    readonly Border _badge;
    readonly RectangleGeometry _clip = new();
    readonly InputCursor _cursorArrow = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    readonly InputCursor _cursorEw = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    readonly InputCursor _cursorHand = InputSystemCursor.Create(InputSystemCursorShape.Hand);

    static readonly SolidColorBrush WaveBrush = BrushOf(170, 230, 230, 230);
    static readonly SolidColorBrush WaveLineBrush = BrushOf(255, 236, 236, 236);
    static readonly SolidColorBrush SampleBrush = BrushOf(255, 255, 255, 255);
    static readonly SolidColorBrush ZeroBrush = BrushOf(80, 255, 255, 255);
    static readonly SolidColorBrush LineBrush = BrushOf(230, 255, 255, 255);
    static readonly SolidColorBrush HotBrush = BrushOf(255, 251, 191, 36);
    static readonly SolidColorBrush TickBrush = BrushOf(255, 209, 213, 219);
    static readonly SolidColorBrush SepBrush = BrushOf(255, 42, 42, 42);
    static readonly SolidColorBrush TextBrush = BrushOf(255, 249, 250, 251);
    static readonly SolidColorBrush LoudBrush = BrushOf(255, 153, 246, 228);
    static readonly SolidColorBrush LoudBackBrush = BrushOf(255, 14, 14, 14);
    static readonly Dictionary<string, SolidColorBrush> WaveFills = [];
    static readonly Dictionary<string, SolidColorBrush> SpecFills = [];

    WavFile? _file;
    ImageSource? _spec;
    double _specT0;
    double _specT1;
    double[] _lo = [];
    double[] _hi = [];
    double[] _loudness = [];
    double _loudPeak;
    double _wavePeak = 0.2;
    double _waveT0;
    double _waveT1 = 1;
    bool _samples;
    int _sampleRate;
    double _view0;
    double _view1 = 1;
    bool _viewportArmed;
    (double T0, double T1, int Columns, int Rows) _notified;

    Image? _specImage;
    Polygon? _wave;
    Polyline? _waveLine;
    readonly List<Ellipse> _dots = [];
    Line? _zero;
    Rectangle? _separator;
    Rectangle? _loudnessBack;
    Rectangle? _loudnessSep;
    Polyline? _loudnessLine;
    Border? _loudnessLabel;
    readonly List<Rectangle> _waveRects = [];
    readonly List<Rectangle> _specRects = [];
    readonly List<TextBlock> _waveTexts = [];
    readonly List<TextBlock> _specTexts = [];
    readonly List<Line> _lines = [];
    readonly List<TextBlock> _ticks = [];
    List<List<(int Index, bool IsStart)>> _groups = [];

    DragKind _drag = DragKind.None;
    int _dragIndex = -1;
    double _panX;
    double _panView0;
    double _panView1;
    List<(double Start, double End)> _origin = [];
    bool _changed;
    object? _zoomToken;
    Pointer? _pointer;

    public SpanEditor()
    {
        var root = new Grid();
        _plot.Background = BrushOf(255, 22, 22, 22);
        _plot.ManipulationMode = ManipulationModes.None;
        _plot.Clip = _clip;
        _readout.IsHitTestVisible = false;
        _readout.FontSize = 13;
        _readout.Foreground = TextBrush;
        _badge = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12),
            Padding = new Thickness(8, 4, 8, 4),
            Background = BrushOf(190, 22, 22, 22),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = _readout,
        };
        root.Children.Add(_plot);
        root.Children.Add(_badge);
        Content = root;
        Background = BrushOf(255, 22, 22, 22);
        ManipulationMode = ManipulationModes.None;
        IsTabStop = true;

        SizeChanged += (_, e) =>
        {
            _plot.Width = e.NewSize.Width;
            _plot.Height = e.NewSize.Height;
            _clip.Rect = new Rect(0, 0, Math.Max(0, e.NewSize.Width), Math.Max(0, e.NewSize.Height));
            ArrangeAll();
        };
        _plot.PointerPressed += OnPointerPressed;
        _plot.PointerMoved += OnPointerMoved;
        _plot.PointerReleased += OnPointerReleased;
        _plot.PointerCanceled += OnPointerReleased;
        _plot.PointerCaptureLost += OnPointerCaptureLost;
        AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnWheel), true);
        PointerEntered += (_, _) => Focus(FocusState.Pointer);
        DoubleTapped += (_, e) =>
        {
            if (_file == null)
                return;
            FinishDrag();
            SetFullView();
            ArrangeAll();
            e.Handled = true;
        };
        PointerExited += (_, _) =>
        {
            if (_drag == DragKind.None)
                SetReadout("");
        };
        KeyDown += OnKeyDown;
    }

    void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_file == null || _drag != DragKind.None)
            return;
        if (e.Key == Windows.System.VirtualKey.Left)
        {
            Nudge(-0.2);
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Right)
        {
            Nudge(0.2);
            e.Handled = true;
        }
    }

    void Nudge(double fraction)
    {
        var span = _view1 - _view0;
        SetView(_view0 + span * fraction, _view1 + span * fraction);
        ArrangeAll();
    }

    public void ZoomIn() => ZoomBy(0.8);

    public void ZoomOut() => ZoomBy(1.25);

    public void ZoomReset()
    {
        if (_file == null)
            return;
        FinishDrag();
        SetFullView();
        ArrangeAll();
    }

    public bool ZoomAt(double x, int delta, object? token = null)
    {
        if (token != null && ReferenceEquals(token, _zoomToken))
            return true;
        if (_file == null || delta == 0 || _drag != DragKind.None || _plot.Width < 2)
            return false;
        _zoomToken = token;
        ZoomBy(delta > 0 ? 0.8 : 1.25, x);
        return true;
    }

    public event EventHandler? Edited;

    public event EventHandler? ViewportChanged;

    public bool HasContent => _file != null;

    public void Clear(string message)
    {
        FinishDrag();
        _file = null;
        _spec = null;
        _lo = [];
        _hi = [];
        _loudness = [];
        _samples = false;
        _sampleRate = 0;
        _wavePeak = 0.2;
        _viewportArmed = false;
        _dots.Clear();
        _plot.Children.Clear();
        _waveRects.Clear();
        _specRects.Clear();
        _waveTexts.Clear();
        _specTexts.Clear();
        _lines.Clear();
        _ticks.Clear();
        _groups.Clear();
        _specImage = null;
        _wave = null;
        _waveLine = null;
        _zero = null;
        _separator = null;
        _loudnessBack = null;
        _loudnessSep = null;
        _loudnessLine = null;
        _loudnessLabel = null;
        SetReadout(message);
        ProtectedCursor = _cursorArrow;
    }

    public void SetFile(
        WavFile file,
        ImageSource? spectrogram,
        double specT0,
        double specT1,
        double[] waveLo,
        double[] waveHi,
        double[] loudness,
        double waveT0,
        double waveT1,
        bool samples,
        int sampleRate)
    {
        FinishDrag();
        _file = file;
        _spec = spectrogram;
        _specT0 = specT0;
        _specT1 = specT1;
        _lo = waveLo;
        _hi = waveHi;
        _loudness = loudness;
        _loudPeak = -80;
        foreach (var value in loudness)
        {
            if (value > _loudPeak)
                _loudPeak = value;
        }

        _wavePeak = 0.2;
        var count = Math.Min(waveLo.Length, waveHi.Length);
        for (var i = 0; i < count; i++)
            _wavePeak = Math.Max(_wavePeak, Math.Max(Math.Abs(waveLo[i]), Math.Abs(waveHi[i])));
        AdoptWave(waveT0, waveT1, samples, sampleRate);
        _viewportArmed = false;
        SetFullView();
        Rebuild();
        SetReadout("");
    }

    public void ShowDetail(
        ImageSource? spectrogram,
        double specT0,
        double specT1,
        double[] waveLo,
        double[] waveHi,
        double waveT0,
        double waveT1,
        bool samples,
        int sampleRate)
    {
        if (_file == null)
            return;
        _spec = spectrogram;
        _specT0 = specT0;
        _specT1 = specT1;
        if (_specImage != null)
            _specImage.Source = spectrogram;
        _lo = waveLo;
        _hi = waveHi;
        AdoptWave(waveT0, waveT1, samples, sampleRate);
        ArrangeAll();
    }

    void AdoptWave(double waveT0, double waveT1, bool samples, int sampleRate)
    {
        if (waveT1 > waveT0 + 1e-9)
        {
            _waveT0 = waveT0;
            _waveT1 = waveT1;
        }
        else
        {
            _waveT0 = 0;
            _waveT1 = Duration;
        }

        _samples = samples;
        if (sampleRate > 0)
            _sampleRate = sampleRate;
    }

    void Rebuild()
    {
        _plot.Children.Clear();
        _dots.Clear();
        _waveRects.Clear();
        _specRects.Clear();
        _waveTexts.Clear();
        _specTexts.Clear();
        _lines.Clear();
        _ticks.Clear();
        if (_file == null)
            return;

        _specImage = new Image
        {
            Source = _spec,
            Stretch = Stretch.Fill,
            UseLayoutRounding = true,
            IsHitTestVisible = false,
        };
        _plot.Children.Add(_specImage);

        _wave = new Polygon
        {
            Fill = WaveBrush,
            IsHitTestVisible = false,
        };
        _plot.Children.Add(_wave);
        _waveLine = new Polyline
        {
            Stroke = WaveLineBrush,
            StrokeThickness = 1.25,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
        _plot.Children.Add(_waveLine);

        _zero = new Line
        {
            Stroke = ZeroBrush,
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        _plot.Children.Add(_zero);

        _separator = new Rectangle
        {
            Height = 1,
            Fill = SepBrush,
            IsHitTestVisible = false,
        };
        _plot.Children.Add(_separator);

        _loudnessBack = new Rectangle
        {
            Fill = LoudBackBrush,
            IsHitTestVisible = false,
        };
        _plot.Children.Add(_loudnessBack);

        _loudnessSep = new Rectangle
        {
            Height = 1,
            Fill = SepBrush,
            IsHitTestVisible = false,
        };
        _plot.Children.Add(_loudnessSep);

        _loudnessLine = new Polyline
        {
            Stroke = LoudBrush,
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
        _plot.Children.Add(_loudnessLine);

        _loudnessLabel = new Border
        {
            Background = BrushOf(200, 14, 14, 14),
            Padding = new Thickness(4, 0, 4, 0),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "大きさ",
                FontSize = 11,
                Foreground = TickBrush,
            },
        };
        foreach (var span in _file.Spans)
        {
            var silence = span.Label == "無音";
            var waveRect = new Rectangle
            {
                Fill = silence ? null : FillBrush(span.Label, wave: true),
                ManipulationMode = ManipulationModes.None,
                Visibility = silence ? Visibility.Collapsed : Visibility.Visible,
            };
            var specRect = new Rectangle
            {
                Fill = silence ? null : FillBrush(span.Label, wave: false),
                ManipulationMode = ManipulationModes.None,
                Visibility = silence ? Visibility.Collapsed : Visibility.Visible,
            };
            var waveText = MakeLabel(span);
            var specText = MakeLabel(span);
            if (silence)
            {
                waveText.Visibility = Visibility.Collapsed;
                specText.Visibility = Visibility.Collapsed;
            }
            _waveRects.Add(waveRect);
            _specRects.Add(specRect);
            _waveTexts.Add(waveText);
            _specTexts.Add(specText);
            _plot.Children.Add(waveRect);
            _plot.Children.Add(specRect);
        }

        _groups = BuildGroups(_file.Spans);
        foreach (var _ in _groups)
        {
            var line = new Line
            {
                Stroke = LineBrush,
                StrokeThickness = 2,
                IsHitTestVisible = false,
            };
            _lines.Add(line);
            _plot.Children.Add(line);
        }

        foreach (var text in _waveTexts)
            _plot.Children.Add(text);
        foreach (var text in _specTexts)
            _plot.Children.Add(text);
        _plot.Children.Add(_loudnessLabel);

        ArrangeAll();
    }

    void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_file == null || _plot.Width < 2)
            return;
        var point = e.GetCurrentPoint(_plot);
        var x = point.Position.X;
        _pointer = e.Pointer;
        if (point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed)
        {
            _drag = DragKind.Pan;
            _panX = x;
            _panView0 = _view0;
            _panView1 = _view1;
            _changed = false;
            ArmCapture(e.Pointer);
            ProtectedCursor = _cursorHand;
            e.Handled = true;
            return;
        }

        var group = HitGroup(x);
        if (group >= 0)
        {
            _drag = DragKind.Boundary;
            _dragIndex = group;
            _origin = Snapshot();
            _changed = false;
            ArmCapture(e.Pointer);
            ProtectedCursor = _cursorEw;
            Highlight(group);
            ShowBoundary(group);
            e.Handled = true;
        }
    }

    void ArmCapture(Pointer pointer)
    {
        _plot.CapturePointer(pointer);
    }

    void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_file == null)
            return;
        var point = e.GetCurrentPoint(_plot);
        var x = point.Position.X;
        if (_drag == DragKind.Pan)
        {
            Pan(x);
            e.Handled = true;
            return;
        }

        if (_drag == DragKind.Boundary)
        {
            ApplyBoundary(XToTime(x));
            e.Handled = true;
            return;
        }

        var group = HitGroup(x);
        if (group >= 0)
        {
            ProtectedCursor = _cursorEw;
            Highlight(group);
            ShowBoundary(group);
            return;
        }

        Highlight(-1);
        var span = HitSpan(XToTime(x));
        ProtectedCursor = _cursorArrow;
        if (span >= 0)
            ShowSpan(span);
        else
            SetReadout("");
    }

    void SetReadout(string text)
    {
        _readout.Text = text;
        _badge.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _pointer = e.Pointer;
        FinishDrag();
        e.Handled = true;
    }

    void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == DragKind.None)
            return;
        _pointer = e.Pointer;
        FinishDrag();
    }

    void FinishDrag()
    {
        if (_drag == DragKind.None)
            return;
        var changed = _changed;
        _drag = DragKind.None;
        _origin = [];
        if (_pointer != null)
        {
            var pointer = _pointer;
            _pointer = null;
            if (_plot.PointerCaptures != null)
            {
                foreach (var captured in _plot.PointerCaptures)
                {
                    if (captured.PointerId == pointer.PointerId)
                    {
                        _plot.ReleasePointerCapture(pointer);
                        break;
                    }
                }
            }
        }

        if (changed)
            Edited?.Invoke(this, EventArgs.Empty);
    }

    void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_plot);
        if (ZoomAt(point.Position.X, point.Properties.MouseWheelDelta, e))
            e.Handled = true;
    }

    void ZoomBy(double factor, double? anchorX = null)
    {
        if (_file == null || _plot.Width < 2)
            return;
        var x = anchorX ?? _plot.Width / 2;
        var anchor = XToTime(x);
        var current = Math.Max(1e-12, _view1 - _view0);
        var span = current * factor;
        var ratio = (anchor - _view0) / current;
        var before0 = _view0;
        var before1 = _view1;
        SetView(anchor - span * ratio, anchor - span * ratio + span);
        if (Math.Abs(_view0 - before0) < 1e-9 && Math.Abs(_view1 - before1) < 1e-9)
            return;
        ArrangeAll();
    }

    void ApplyBoundary(double time)
    {
        if (_file == null || _dragIndex < 0 || _dragIndex >= _groups.Count)
            return;
        Restore(_origin);
        if (_dragIndex >= _groups.Count)
            return;
        var (lo, hi) = Limits(_origin, _groups, _dragIndex, Duration);
        if (hi < lo)
            hi = lo;
        time = Math.Clamp(time, lo, hi);
        foreach (var (index, isStart) in _groups[_dragIndex])
        {
            if (isStart)
                _file.Spans[index].Start = time;
            else
                _file.Spans[index].End = time;
        }

        _changed = Changed();
        ArrangeSpans();
        Highlight(_dragIndex);
        ShowBoundary(_dragIndex);
    }

    void Pan(double x)
    {
        var span = _panView1 - _panView0;
        var dt = (x - _panX) / Math.Max(1, PlotWidth) * span;
        SetView(_panView0 - dt, _panView1 - dt);
        ArrangeAll();
    }

    void ArrangeAll()
    {
        if (_file == null || _plot.Width < 2 || _plot.Height < 2)
            return;
        ArrangeWave();
        ArrangeSpans();
        ArrangeTicks();
        NotifyViewport();
    }

    void ArrangeWave()
    {
        if (_specImage != null)
        {
            Canvas.SetLeft(_specImage, X(_specT0));
            Canvas.SetTop(_specImage, SpecTop);
            _specImage.Width = Math.Max(1, X(_specT1) - X(_specT0));
            _specImage.Height = Math.Max(1, SpecHeight);
        }

        ArrangeWaveform();

        if (_zero != null)
        {
            var y = WaveHeight * 0.5;
            _zero.X1 = Pad;
            _zero.X2 = Math.Max(Pad, _plot.Width - Pad);
            _zero.Y1 = y;
            _zero.Y2 = y;
        }

        if (_separator != null)
        {
            Canvas.SetLeft(_separator, 0);
            Canvas.SetTop(_separator, WaveHeight);
            _separator.Width = _plot.Width;
        }

        ArrangeLoudness();
    }

    void ArrangeLoudness()
    {
        var height = LoudnessHeight;
        var top = LoudnessTop;
        var visible = height > 1;
        if (_loudnessBack != null)
        {
            Canvas.SetLeft(_loudnessBack, 0);
            Canvas.SetTop(_loudnessBack, top);
            _loudnessBack.Width = _plot.Width;
            _loudnessBack.Height = Math.Max(0, height);
            _loudnessBack.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_loudnessSep != null)
        {
            Canvas.SetLeft(_loudnessSep, 0);
            Canvas.SetTop(_loudnessSep, top);
            _loudnessSep.Width = _plot.Width;
            _loudnessSep.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_loudnessLabel != null)
        {
            Canvas.SetLeft(_loudnessLabel, Pad + 2);
            Canvas.SetTop(_loudnessLabel, top + 3);
            _loudnessLabel.Visibility = height > 22 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_loudnessLine == null)
            return;
        var count = _loudness.Length;
        if (!visible || count < 2 || _file == null)
        {
            _loudnessLine.Points = new PointCollection();
            return;
        }

        var points = new PointCollection();
        for (var i = 0; i < count; i++)
        {
            var t = Duration * (i + 0.5) / count;
            points.Add(new Point(X(t), LoudnessY(_loudness[i])));
        }

        _loudnessLine.Points = points;
    }

    void ArrangeSpans()
    {
        if (_file == null)
            return;
        var waveTop = 0.0;
        var waveHeight = WaveHeight;
        var specTop = SpecTop;
        var specHeight = SpecHeight;
        var spans = _file.Spans;
        for (var i = 0; i < spans.Count; i++)
        {
            if (spans[i].Label == "無音")
            {
                HideSpanChrome(_waveRects, _waveTexts, i);
                HideSpanChrome(_specRects, _specTexts, i);
                continue;
            }

            var left = X(spans[i].Start);
            var right = X(spans[i].End);
            var width = Math.Max(0, right - left);
            PlaceRect(_waveRects, i, left, waveTop, width, waveHeight);
            PlaceRect(_specRects, i, left, specTop, width, specHeight);
            PlaceText(_waveTexts, i, left, waveTop + 8, width);
            PlaceText(_specTexts, i, left, specTop + 8, width);
        }

        for (var i = 0; i < _lines.Count && i < _groups.Count; i++)
        {
            if (IsOuterSilenceEdge(i))
            {
                _lines[i].Visibility = Visibility.Collapsed;
                continue;
            }

            _lines[i].Visibility = Visibility.Visible;
            var x = X(GroupTime(i));
            _lines[i].X1 = x;
            _lines[i].X2 = x;
            _lines[i].Y1 = 0;
            _lines[i].Y2 = Math.Max(0, _plot.Height - Ruler);
        }
    }

    void ArrangeTicks()
    {
        foreach (var tick in _ticks)
            _plot.Children.Remove(tick);
        _ticks.Clear();
        var span = Math.Max(1e-6, _view1 - _view0);
        var step = NiceStep(span / Math.Max(1, PlotWidth / 90));
        var first = Math.Ceiling(_view0 / step) * step;
        var y = _plot.Height - Ruler + 1;
        for (var t = first; t <= _view1 + step * 0.001; t += step)
        {
            var tick = new TextBlock
            {
                Text = t.ToString(step >= 0.01 ? "0.00" : step >= 0.001 ? "0.000" : "0.0000"),
                Foreground = TickBrush,
                FontSize = 11,
                IsHitTestVisible = false,
            };
            _ticks.Add(tick);
            _plot.Children.Add(tick);
            Canvas.SetLeft(tick, X(t) + 4);
            Canvas.SetTop(tick, y);
        }
    }

    void ArrangeWaveform()
    {
        var count = Math.Min(_lo.Length, _hi.Length);
        if (_wave == null || count == 0 || _file == null)
        {
            ClearDots();
            if (_wave != null)
                _wave.Points = new PointCollection();
            if (_waveLine != null)
                _waveLine.Points = new PointCollection();
            return;
        }

        if (_samples)
        {
            _wave.Points = new PointCollection();
            var points = new PointCollection();
            for (var i = 0; i < count; i++)
                points.Add(new Point(X(BinTime(i, count)), WaveY(_hi[i])));
            if (_waveLine != null)
            {
                _waveLine.Points = points;
                _waveLine.Visibility = Visibility.Visible;
            }

            PlaceDots(points);
            return;
        }

        ClearDots();
        if (_waveLine != null)
        {
            _waveLine.Points = new PointCollection();
            _waveLine.Visibility = Visibility.Collapsed;
        }

        _wave.Points = WavePoints();
    }

    void PlaceDots(PointCollection points)
    {
        if (_waveLine == null || points.Count == 0)
        {
            ClearDots();
            return;
        }

        var spacing = points.Count >= 2 ? Math.Abs(points[1].X - points[0].X) : 12;
        if (spacing < 4 || points.Count > 2500)
        {
            ClearDots();
            return;
        }

        var diameter = Math.Clamp(spacing * 0.42, 3.5, 10);
        while (_dots.Count > points.Count)
        {
            var extra = _dots[^1];
            _dots.RemoveAt(_dots.Count - 1);
            _plot.Children.Remove(extra);
        }

        var insertAt = _plot.Children.IndexOf(_waveLine) + 1;
        if (insertAt <= 0)
            insertAt = _plot.Children.Count;
        while (_dots.Count < points.Count)
        {
            var dot = new Ellipse
            {
                Fill = SampleBrush,
                IsHitTestVisible = false,
            };
            _dots.Add(dot);
            _plot.Children.Insert(insertAt + _dots.Count - 1, dot);
        }

        for (var i = 0; i < points.Count; i++)
        {
            var dot = _dots[i];
            dot.Width = diameter;
            dot.Height = diameter;
            Canvas.SetLeft(dot, points[i].X - diameter / 2);
            Canvas.SetTop(dot, points[i].Y - diameter / 2);
        }
    }

    void ClearDots()
    {
        foreach (var dot in _dots)
            _plot.Children.Remove(dot);
        _dots.Clear();
    }

    PointCollection WavePoints()
    {
        var points = new PointCollection();
        var count = Math.Min(_lo.Length, _hi.Length);
        if (count == 0 || _file == null)
            return points;
        for (var i = 0; i < count; i++)
            points.Add(new Point(X(BinTime(i, count)), WaveY(_hi[i])));
        for (var i = count - 1; i >= 0; i--)
            points.Add(new Point(X(BinTime(i, count)), WaveY(_lo[i])));
        return points;
    }

    double BinTime(int index, int count)
    {
        if (count <= 0)
            return _waveT0;
        var span = _waveT1 - _waveT0;
        if (span <= 0)
            return _waveT0;
        return _waveT0 + (index + 0.5) / count * span;
    }

    void ShowBoundary(int group)
    {
        if (group < 0 || group >= _groups.Count)
            return;
        SetReadout($"{GroupTime(group):0.000} 秒");
    }

    void ShowSpan(int index)
    {
        if (_file == null || index < 0 || index >= _file.Spans.Count)
            return;
        var span = _file.Spans[index];
        SetReadout($"{Caption(span)}   {span.Start:0.000}–{span.End:0.000}");
    }

    void Highlight(int group)
    {
        for (var i = 0; i < _lines.Count; i++)
            _lines[i].Stroke = i == group ? HotBrush : LineBrush;
    }

    int HitGroup(double x)
    {
        var best = -1;
        var bestDist = HitPx;
        for (var i = 0; i < _groups.Count; i++)
        {
            if (IsOuterSilenceEdge(i))
                continue;
            var dist = Math.Abs(X(GroupTime(i)) - x);
            if (dist <= bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        return best;
    }

    int HitSpan(double time)
    {
        if (_file == null)
            return -1;
        var best = -1;
        var bestWidth = double.MaxValue;
        var spans = _file.Spans;
        for (var i = 0; i < spans.Count; i++)
        {
            if (time < spans[i].Start - 1e-9 || time > spans[i].End + 1e-9)
                continue;
            var width = spans[i].End - spans[i].Start;
            if (width < bestWidth)
            {
                bestWidth = width;
                best = i;
            }
        }

        return best;
    }

    bool IsOuterSilenceEdge(int group)
    {
        if (_file == null || group < 0 || group >= _groups.Count)
            return false;
        var spans = _file.Spans;
        var members = _groups[group];
        if (spans.Count == 0 || members.Count == 0)
            return false;
        foreach (var (index, isStart) in members)
        {
            var leading = index == 0 && spans[0].Label == "無音" && isStart;
            var trailing = index == spans.Count - 1 && spans[^1].Label == "無音" && !isStart;
            if (!leading && !trailing)
                return false;
        }

        return true;
    }

    double GroupTime(int group)
    {
        if (_file == null || group < 0 || group >= _groups.Count || _groups[group].Count == 0)
            return 0;
        var (index, isStart) = _groups[group][0];
        var span = _file.Spans[index];
        return isStart ? span.Start : span.End;
    }

    List<(double Start, double End)> Snapshot()
    {
        var copy = new List<(double Start, double End)>();
        if (_file == null)
            return copy;
        foreach (var span in _file.Spans)
            copy.Add((span.Start, span.End));
        return copy;
    }

    void Restore(List<(double Start, double End)> origin)
    {
        if (_file == null)
            return;
        var count = Math.Min(origin.Count, _file.Spans.Count);
        for (var i = 0; i < count; i++)
        {
            _file.Spans[i].Start = origin[i].Start;
            _file.Spans[i].End = origin[i].End;
        }
    }

    bool Changed()
    {
        if (_file == null || _origin.Count != _file.Spans.Count)
            return false;
        for (var i = 0; i < _origin.Count; i++)
        {
            if (Math.Abs(_file.Spans[i].Start - _origin[i].Start) > 1e-6)
                return true;
            if (Math.Abs(_file.Spans[i].End - _origin[i].End) > 1e-6)
                return true;
        }

        return false;
    }

    double Duration => _file == null || _file.Duration <= 0 ? 1e-3 : _file.Duration;

    double PlotWidth => Math.Max(1, _plot.Width - Pad * 2);

    double ContentBottom => Math.Max(0, _plot.Height - Ruler);

    double WaveHeight => ContentBottom * 0.40;

    double SpecTop => WaveHeight;

    double SpecBlockHeight => Math.Max(1, ContentBottom - SpecTop);

    double LoudnessHeight
    {
        get
        {
            if (_loudness.Length < 2)
                return 0;
            var wanted = Math.Clamp(SpecBlockHeight * 0.28, 44, 110);
            return Math.Min(wanted, SpecBlockHeight * 0.42);
        }
    }

    double LoudnessTop => SpecTop + SpecHeight;

    double SpecHeight => Math.Max(1, SpecBlockHeight - LoudnessHeight);

    double X(double time)
    {
        return Pad + (time - _view0) / Math.Max(1e-9, _view1 - _view0) * PlotWidth;
    }

    double XToTime(double x)
    {
        return _view0 + (x - Pad) / PlotWidth * (_view1 - _view0);
    }

    double WaveY(double sample)
    {
        var mid = WaveHeight * 0.5;
        var amp = Math.Max(1, mid - 6);
        var peak = _wavePeak > 1e-9 ? _wavePeak : 0.2;
        return mid - sample / peak * amp;
    }

    double LoudnessY(double db)
    {
        var top = LoudnessTop + 5;
        var bottom = LoudnessTop + LoudnessHeight - 3;
        var level = Math.Clamp((db - (_loudPeak - LoudnessRangeDb)) / LoudnessRangeDb, 0, 1);
        return bottom - level * Math.Max(1, bottom - top);
    }

    void SetFullView()
    {
        _view0 = 0;
        _view1 = Duration;
    }

    void SetView(double start, double end)
    {
        var duration = Duration;
        var minSpan = Math.Min(MinViewSpan(), duration);
        var span = Math.Clamp(end - start, minSpan, duration);
        start = Math.Clamp(start, 0, Math.Max(0, duration - span));
        _view0 = start;
        _view1 = start + span;
    }

    double MinViewSpan()
    {
        var sr = _sampleRate > 0 ? _sampleRate : 44100.0;
        var samples = Math.Max(8, PlotWidth / 8.0);
        return Math.Min(Duration, samples / sr);
    }

    public (double T0, double T1, int Columns, int Rows) ViewportQuery()
    {
        var (columns, rows) = RasterSize();
        var t1 = _file == null ? 0 : _view1;
        return (_view0, t1, columns, rows);
    }

    public (int Columns, int Rows) RasterSize()
    {
        var scale = 1.0;
        if (XamlRoot != null && XamlRoot.RasterizationScale > 0)
            scale = XamlRoot.RasterizationScale;
        var plot = _plot.Width >= 2 ? PlotWidth : 1400;
        var spec = _plot.Height >= 2 ? SpecHeight : 360;
        var columns = (int)Math.Clamp(Math.Ceiling(Math.Max(1, plot) * scale), 64, 4096);
        var rows = (int)Math.Clamp(Math.Ceiling(Math.Max(1, spec) * scale), 64, 1200);
        return (columns, rows);
    }

    void NotifyViewport()
    {
        if (_file == null)
            return;
        var query = ViewportQuery();
        if (_viewportArmed && query == _notified)
            return;
        _viewportArmed = true;
        _notified = query;
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    static void HideSpanChrome(List<Rectangle> rects, List<TextBlock> texts, int index)
    {
        if (index < rects.Count)
            rects[index].Visibility = Visibility.Collapsed;
        if (index < texts.Count)
            texts[index].Visibility = Visibility.Collapsed;
    }

    static void PlaceRect(List<Rectangle> rects, int index, double left, double top, double width, double height)
    {
        if (index >= rects.Count)
            return;
        var rect = rects[index];
        Canvas.SetLeft(rect, left);
        Canvas.SetTop(rect, top);
        rect.Width = width;
        rect.Height = Math.Max(0, height);
    }

    static void PlaceText(List<TextBlock> texts, int index, double left, double top, double width)
    {
        if (index >= texts.Count)
            return;
        var text = texts[index];
        text.Width = Math.Max(0, width);
        text.Visibility = width < 28 ? Visibility.Collapsed : Visibility.Visible;
        Canvas.SetLeft(text, left);
        Canvas.SetTop(text, top);
    }

    static TextBlock MakeLabel(PhoneSpan span)
    {
        return new TextBlock
        {
            Text = Caption(span),
            Foreground = TextBrush,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false,
        };
    }

    static string Caption(PhoneSpan span)
    {
        if (string.IsNullOrEmpty(span.Token) || span.Token == "_" || span.Token == span.Label)
            return span.Label;
        return span.Token + "  " + span.Label;
    }

    static List<List<(int Index, bool IsStart)>> BuildGroups(List<PhoneSpan> spans)
    {
        var edges = new List<(double Time, int Index, bool IsStart)>(spans.Count * 2);
        for (var i = 0; i < spans.Count; i++)
        {
            edges.Add((spans[i].Start, i, true));
            edges.Add((spans[i].End, i, false));
        }

        return Cluster(edges);
    }

    static List<List<(int Index, bool IsStart)>> Cluster(List<(double Time, int Index, bool IsStart)> edges)
    {
        edges.Sort((a, b) => a.Time.CompareTo(b.Time));
        var groups = new List<List<(int Index, bool IsStart)>>();
        var groupTime = double.NegativeInfinity;
        foreach (var edge in edges)
        {
            if (groups.Count > 0 && edge.Time - groupTime <= LinkGap)
            {
                groups[^1].Add((edge.Index, edge.IsStart));
                continue;
            }

            groupTime = edge.Time;
            groups.Add([(edge.Index, edge.IsStart)]);
        }

        return groups;
    }

    static (double Lo, double Hi) Limits(
        List<(double Start, double End)> spans,
        List<List<(int Index, bool IsStart)>> groups,
        int index,
        double duration)
    {
        var group = groups[index];
        var time = spans[group[0].Index].Start;
        if (!group[0].IsStart)
            time = spans[group[0].Index].End;
        var lo = 0.0;
        var hi = duration;
        foreach (var (spanIndex, isStart) in group)
        {
            if (isStart)
                hi = Math.Min(hi, spans[spanIndex].End - MinSpan);
            else
                lo = Math.Max(lo, spans[spanIndex].Start + MinSpan);
        }

        for (var other = 0; other < groups.Count; other++)
        {
            if (other == index || groups[other].Count == 0)
                continue;
            var (spanIndex, isStart) = groups[other][0];
            var otherTime = isStart ? spans[spanIndex].Start : spans[spanIndex].End;
            if (otherTime < time)
                lo = Math.Max(lo, otherTime + MinSpan);
            else if (otherTime > time)
                hi = Math.Min(hi, otherTime - MinSpan);
        }

        return (lo, hi);
    }

    static double NiceStep(double target)
    {
        double[] steps = [0.0001, 0.0002, 0.0005, 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10];
        foreach (var step in steps)
        {
            if (step >= target)
                return step;
        }

        return 10;
    }

    static SolidColorBrush FillBrush(string label, bool wave)
    {
        var cache = wave ? WaveFills : SpecFills;
        if (cache.TryGetValue(label, out var brush))
            return brush;
        var color = KindColor(label);
        brush = new SolidColorBrush(Color.FromArgb(wave ? (byte)70 : (byte)88, color.R, color.G, color.B));
        cache[label] = brush;
        return brush;
    }

    static Color KindColor(string label)
    {
        if (label == "無音")
            return Color.FromArgb(255, 154, 160, 166);
        if (label == "息")
            return Color.FromArgb(255, 96, 165, 250);
        if (label is "a" or "i" or "u" or "e" or "o" or "N" or "M")
            return Color.FromArgb(255, 15, 157, 142);
        return Color.FromArgb(255, 217, 119, 6);
    }

    static SolidColorBrush BrushOf(byte a, byte r, byte g, byte b)
    {
        return new SolidColorBrush(Color.FromArgb(a, r, g, b));
    }
}
