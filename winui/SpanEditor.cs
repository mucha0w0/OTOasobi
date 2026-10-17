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
    const double SplitThickness = 8;
    const double SplitSlop = 10;
    const double MinBand = 48;

    enum DragKind
    {
        None,
        Boundary,
        Pan,
        Split,
    }

    enum SplitLook
    {
        Rest,
        Hover,
        Drag,
    }

    readonly CursorCanvas _plot = new();
    readonly TextBlock _readout = new();
    readonly Border _badge;
    readonly RectangleGeometry _clip = new();
    readonly InputCursor _cursorArrow = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    readonly InputCursor _cursorEw = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    readonly InputCursor _cursorNs = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
    readonly InputCursor _cursorHand = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    readonly Grid _waveSplit = MakeSplitBar();
    readonly Grid _loudSplit = MakeSplitBar();
    readonly Grid _otoSplit = MakeSplitBar();

    static readonly SolidColorBrush WaveBrush = BrushOf(170, 230, 230, 230);
    static readonly SolidColorBrush WaveLineBrush = BrushOf(255, 236, 236, 236);
    static readonly SolidColorBrush SampleBrush = BrushOf(255, 255, 255, 255);
    static readonly SolidColorBrush ZeroBrush = BrushOf(80, 255, 255, 255);
    static readonly SolidColorBrush LineBrush = BrushOf(230, 255, 255, 255);
    static readonly SolidColorBrush HotBrush = BrushOf(255, 251, 191, 36);
    static readonly SolidColorBrush TickBrush = BrushOf(255, 209, 213, 219);
    static readonly SolidColorBrush TextBrush = BrushOf(255, 249, 250, 251);
    static readonly SolidColorBrush SplitLineBrush = BrushOf(0x24, 0xFF, 0xFF, 0xFF);
    static readonly SolidColorBrush LoudBrush = BrushOf(255, 153, 246, 228);
    static readonly SolidColorBrush LoudBackBrush = BrushOf(255, 14, 14, 14);
    static readonly SolidColorBrush OtoRegionBrush = BrushOf(64, 15, 157, 142);
    static readonly SolidColorBrush OtoRegionHotBrush = BrushOf(120, 45, 212, 191);
    static readonly SolidColorBrush OtoChipBrush = BrushOf(190, 14, 14, 14);
    const int OtoPointCount = 5;
    const double OtoDimOpacity = 0.28;
    static readonly SolidColorBrush[] OtoPointBrushes =
    [
        BrushOf(255, 236, 236, 236),
        BrushOf(255, 251, 146, 60),
        BrushOf(255, 248, 113, 113),
        BrushOf(255, 96, 165, 250),
        BrushOf(255, 196, 181, 253),
    ];
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
    double _waveShare = 0.30;
    double _loudShare = 0.14;
    double _otoShare = 0.24;
    bool _viewportArmed;
    (double T0, double T1, int Columns, int Rows) _notified;

    Image? _specImage;
    Polygon? _wave;
    Polyline? _waveLine;
    readonly List<Ellipse> _dots = [];
    Line? _zero;
    Rectangle? _loudnessBack;
    Polyline? _loudnessLine;
    Border? _loudnessLabel;
    Polygon? _otoWave;
    Polyline? _otoWaveLine;
    Line? _otoZero;
    Border? _otoCaption;
    readonly List<Rectangle> _waveRects = [];
    readonly List<Rectangle> _specRects = [];
    readonly List<TextBlock> _waveTexts = [];
    readonly List<TextBlock> _specTexts = [];
    readonly List<Line> _lines = [];
    readonly List<TextBlock> _ticks = [];
    readonly List<OtoMark> _otoMarks = [];
    readonly List<Rectangle> _otoRects = [];
    readonly List<TextBlock> _otoTexts = [];
    readonly List<Line> _otoLines = [];
    readonly List<Border> _otoPointChips = [];
    List<List<(int Index, bool IsStart)>> _groups = [];
    OtoExport _oto = new(0, 200, 0, new Dictionary<string, double>());
    int _hotOto = -1;
    int _focusOto = -1;

    DragKind _drag = DragKind.None;
    int _dragIndex = -1;
    int _splitIndex = -1;
    int _hotSplit = -1;
    double _splitY;
    double _splitWave;
    double _splitLoud;
    double _splitOto;
    double _panX;
    double _panView0;
    double _panView1;
    List<(double Start, double End)> _origin = [];
    bool _changed;
    object? _zoomToken;
    Pointer? _pointer;
    bool _labelsLocked;
    readonly Border _labelGate;

    public event EventHandler? AnalyzeRequested;

    public void SetLabelsLocked(bool locked)
    {
        _labelsLocked = locked;
        PlaceLabelGate();
    }

    public SpanEditor()
    {
        var root = new Grid();
        _labelGate = MakeLabelGate();
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
        root.Children.Add(_labelGate);
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
            PlaceLabelGate();
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
            var y = e.GetPosition(_plot).Y;
            if (InOtoBand(y))
            {
                e.Handled = true;
                return;
            }

            FinishDrag();
            SetFullView();
            ArrangeAll();
            e.Handled = true;
        };
        PointerExited += (_, _) =>
        {
            if (_drag != DragKind.None)
                return;
            SetHotOto(-1);
            if (_focusOto >= 0)
                ShowFocusedOto(double.NaN);
            else
                SetReadout("");
            SetHotSplit(-1);
        };
        KeyDown += OnKeyDown;
    }

    void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_file == null || _drag != DragKind.None)
            return;
        if (e.Key == Windows.System.VirtualKey.Escape && _focusOto >= 0)
        {
            SetFocusOto(-1);
            SetReadout("");
            e.Handled = true;
            return;
        }

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
        _otoMarks.Clear();
        _otoRects.Clear();
        _otoTexts.Clear();
        _otoLines.Clear();
        _otoPointChips.Clear();
        _hotOto = -1;
        _focusOto = -1;
        _groups.Clear();
        _specImage = null;
        _wave = null;
        _waveLine = null;
        _zero = null;
        _loudnessBack = null;
        _loudnessLine = null;
        _loudnessLabel = null;
        _otoWave = null;
        _otoWaveLine = null;
        _otoZero = null;
        _otoCaption = null;
        SetReadout(message);
        UseCursor(_cursorArrow);
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

    internal void SetOtoSettings(OtoExport settings)
    {
        if (!string.Equals(settings.Mode, _oto.Mode, StringComparison.Ordinal))
            _focusOto = -1;
        _oto = settings;
        if (_file == null || _plot.Width < 2 || _plot.Height < 2)
            return;
        ArrangeOtoLabels();
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
        _otoMarks.Clear();
        _otoRects.Clear();
        _otoTexts.Clear();
        _otoLines.Clear();
        _otoPointChips.Clear();
        _hotOto = -1;
        _focusOto = -1;
        _otoWave = null;
        _otoWaveLine = null;
        _otoZero = null;
        _otoCaption = null;
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

        _loudnessBack = new Rectangle
        {
            Fill = LoudBackBrush,
            IsHitTestVisible = false,
        };
        _plot.Children.Add(_loudnessBack);

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
            var quiet = IsQuietSpan(span.Label);
            var waveRect = new Rectangle
            {
                Fill = quiet ? null : FillBrush(span.Label, wave: true),
                ManipulationMode = ManipulationModes.None,
                IsHitTestVisible = false,
                Visibility = quiet ? Visibility.Collapsed : Visibility.Visible,
            };
            var specRect = new Rectangle
            {
                Fill = quiet ? null : FillBrush(span.Label, wave: false),
                ManipulationMode = ManipulationModes.None,
                IsHitTestVisible = false,
                Visibility = quiet ? Visibility.Collapsed : Visibility.Visible,
            };
            var waveText = MakeLabel(span);
            var specText = MakeLabel(span);
            if (quiet)
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
        AddOtoVisuals();
        _plot.Children.Add(_loudnessLabel);
        _plot.Children.Add(_waveSplit);
        _plot.Children.Add(_loudSplit);
        _plot.Children.Add(_otoSplit);

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
            UseCursor(_cursorHand);
            e.Handled = true;
            return;
        }

        var split = HitSplit(point.Position.Y);
        if (split >= 0)
        {
            _drag = DragKind.Split;
            _splitIndex = split;
            _splitY = point.Position.Y;
            _splitWave = ShownWaveShare;
            _splitLoud = ShownLoudShare;
            _splitOto = ShownOtoShare;
            _changed = false;
            SetHotSplit(split);
            ArmCapture(e.Pointer);
            UseCursor(_cursorNs);
            e.Handled = true;
            return;
        }

        if (InOtoBand(point.Position.Y))
        {
            FocusOtoAt(x);
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
            UseCursor(_cursorEw);
            Highlight(group);
            ShowBoundary(group);
            e.Handled = true;
        }
    }

    void ArmCapture(Pointer pointer)
    {
        _plot.CapturePointer(pointer);
    }

    void UseCursor(InputCursor cursor)
    {
        if (!ReferenceEquals(_plot.Cursor, cursor))
            _plot.Cursor = cursor;
        if (!ReferenceEquals(ProtectedCursor, cursor))
            ProtectedCursor = cursor;
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
            UseCursor(_cursorHand);
            e.Handled = true;
            return;
        }

        if (_drag == DragKind.Boundary)
        {
            ApplyBoundary(XToTime(x));
            UseCursor(_cursorEw);
            e.Handled = true;
            return;
        }

        if (_drag == DragKind.Split)
        {
            ApplySplit(point.Position.Y);
            UseCursor(_cursorNs);
            e.Handled = true;
            return;
        }

        var split = HitSplit(point.Position.Y);
        if (split >= 0)
        {
            UseCursor(_cursorNs);
            Highlight(-1);
            SetHotOto(-1);
            SetHotSplit(split);
            SetReadout("");
            return;
        }

        SetHotSplit(-1);
        if (InOtoBand(point.Position.Y))
        {
            UseCursor(_cursorHand);
            Highlight(-1);
            if (_focusOto >= 0)
                ShowFocusedOto(x);
            else
                ShowOtoHover(x);
            return;
        }

        SetHotOto(-1);
        var group = HitGroup(x);
        if (group >= 0)
        {
            UseCursor(_cursorEw);
            Highlight(group);
            ShowBoundary(group);
            return;
        }

        Highlight(-1);
        var span = HitSpan(XToTime(x));
        UseCursor(_cursorArrow);
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
        var split = _drag == DragKind.Split;
        _drag = DragKind.None;
        _splitIndex = -1;
        _origin = [];
        if (split)
            SetHotSplit(-1);
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
        PlaceLabelGate();
        NotifyViewport();
    }

    Border MakeLabelGate()
    {
        var button = new Button
        {
            Content = "音声を解析",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 8, 0, 0),
        };
        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var style) && style is Style accent)
            button.Style = accent;
        button.Click += (_, _) => AnalyzeRequested?.Invoke(this, EventArgs.Empty);
        var caption = new TextBlock
        {
            Text = "この機能を使うには、先に音声を解析してください。",
            Foreground = TextBrush,
            FontSize = 14,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var card = new StackPanel
        {
            Spacing = 0,
            MaxWidth = 360,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        card.Children.Add(caption);
        card.Children.Add(button);
        return new Border
        {
            Background = BrushOf(168, 0, 0, 0),
            Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = true,
            Visibility = Visibility.Collapsed,
            Child = card,
        };
    }

    void PlaceLabelGate()
    {
        if (!_labelsLocked || _plot.Height < 2)
        {
            _labelGate.Visibility = Visibility.Collapsed;
            return;
        }

        var height = Math.Max(0, OtoHeight);
        if (height < 8)
        {
            _labelGate.Visibility = Visibility.Collapsed;
            return;
        }

        _labelGate.Margin = new Thickness(0, OtoTop, 0, 0);
        _labelGate.Height = height;
        _labelGate.Visibility = Visibility.Visible;
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

        ArrangeLoudness();
        ArrangeOtoWave();
        ArrangeSplits();
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
            if (IsQuietSpan(spans[i].Label))
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

        ArrangeOtoLabels();

        for (var i = 0; i < _lines.Count && i < _groups.Count; i++)
        {
            if (IsQuietBoundary(i))
            {
                _lines[i].Visibility = Visibility.Collapsed;
                continue;
            }

            _lines[i].Visibility = Visibility.Visible;
            var x = X(GroupTime(i));
            _lines[i].X1 = x;
            _lines[i].X2 = x;
            _lines[i].Y1 = 0;
            _lines[i].Y2 = Math.Max(0, OtoTop - SplitThickness);
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
            var points = SamplePoints(WaveY);
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

        _wave.Points = WavePoints(WaveY);
    }

    void ArrangeOtoWave()
    {
        var count = Math.Min(_lo.Length, _hi.Length);
        var visible = _otoWave != null && count > 0 && _file != null && OtoHeight > 1;
        if (_otoWave == null || _otoWaveLine == null)
            return;
        if (!visible)
        {
            _otoWave.Points = new PointCollection();
            _otoWaveLine.Points = new PointCollection();
            if (_otoZero != null)
                _otoZero.Visibility = Visibility.Collapsed;
            return;
        }

        if (_otoZero != null)
        {
            var y = OtoTop + OtoHeight * 0.5;
            _otoZero.Visibility = Visibility.Visible;
            _otoZero.X1 = Pad;
            _otoZero.X2 = Math.Max(Pad, _plot.Width - Pad);
            _otoZero.Y1 = y;
            _otoZero.Y2 = y;
        }

        if (_samples)
        {
            _otoWave.Points = new PointCollection();
            _otoWaveLine.Points = SamplePoints(OtoY);
            _otoWaveLine.Visibility = Visibility.Visible;
            return;
        }

        _otoWaveLine.Points = new PointCollection();
        _otoWaveLine.Visibility = Visibility.Collapsed;
        _otoWave.Points = WavePoints(OtoY);
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

    PointCollection WavePoints(Func<double, double> yOf)
    {
        var points = new PointCollection();
        var count = Math.Min(_lo.Length, _hi.Length);
        if (count == 0 || _file == null)
            return points;
        for (var i = 0; i < count; i++)
            points.Add(new Point(X(BinTime(i, count)), yOf(_hi[i])));
        for (var i = count - 1; i >= 0; i--)
            points.Add(new Point(X(BinTime(i, count)), yOf(_lo[i])));
        return points;
    }

    PointCollection SamplePoints(Func<double, double> yOf)
    {
        var points = new PointCollection();
        var count = Math.Min(_lo.Length, _hi.Length);
        for (var i = 0; i < count; i++)
            points.Add(new Point(X(BinTime(i, count)), yOf(_hi[i])));
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

    void ApplySplit(double y)
    {
        var area = BandArea;
        if (area < 1)
            return;
        var dy = y - _splitY;
        var bands = HasLoudness ? 4.0 : 3.0;
        var minPx = Math.Min(MinBand, area / bands);
        var wavePx = area * _splitWave;
        var loudPx = HasLoudness ? area * _splitLoud : 0;
        var otoPx = area * _splitOto;
        if (_splitIndex == 0)
        {
            wavePx = ClampRange(wavePx + dy, minPx, area - loudPx - otoPx - minPx);
            _waveShare = wavePx / area;
        }
        else if (_splitIndex == 1)
        {
            loudPx = ClampRange(loudPx - dy, minPx, area - wavePx - otoPx - minPx);
            _loudShare = loudPx / area;
        }
        else if (HasLoudness)
        {
            var pair = loudPx + otoPx;
            var floor = Math.Min(minPx, pair / 2);
            otoPx = ClampRange(otoPx - dy, floor, pair - floor);
            loudPx = pair - otoPx;
            _loudShare = loudPx / area;
            _otoShare = otoPx / area;
        }
        else
        {
            otoPx = ClampRange(otoPx - dy, minPx, area - wavePx - minPx);
            _otoShare = otoPx / area;
        }

        ArrangeAll();
    }

    int HitSplit(double y)
    {
        if (InSplit(y, WaveHeight))
            return 0;
        if (HasLoudness && InSplit(y, LoudnessTop - SplitThickness))
            return 1;
        if (InSplit(y, OtoTop - SplitThickness))
            return 2;
        return -1;
    }

    static bool InSplit(double y, double top)
    {
        return y >= top - SplitSlop && y < top + SplitThickness + SplitSlop;
    }

    void SetHotSplit(int index)
    {
        if (_drag == DragKind.Split)
            index = _splitIndex;
        if (_hotSplit == index)
            return;
        _hotSplit = index;
        PaintSplit(_waveSplit, LookFor(0));
        PaintSplit(_loudSplit, LookFor(1));
        PaintSplit(_otoSplit, LookFor(2));
    }

    void ArrangeSplits()
    {
        PlaceSplit(_waveSplit, WaveHeight, LookFor(0));
        if (HasLoudness)
        {
            _loudSplit.Visibility = Visibility.Visible;
            PlaceSplit(_loudSplit, LoudnessTop - SplitThickness, LookFor(1));
        }
        else
        {
            _loudSplit.Visibility = Visibility.Collapsed;
        }

        _otoSplit.Visibility = Visibility.Visible;
        PlaceSplit(_otoSplit, OtoTop - SplitThickness, LookFor(2));
    }

    SplitLook LookFor(int index)
    {
        if (_drag == DragKind.Split && _splitIndex == index)
            return SplitLook.Drag;
        if (_hotSplit == index)
            return SplitLook.Hover;
        return SplitLook.Rest;
    }

    void PlaceSplit(Grid bar, double top, SplitLook look)
    {
        bar.Width = Math.Max(0, _plot.Width);
        Canvas.SetLeft(bar, 0);
        Canvas.SetTop(bar, top);
        PaintSplit(bar, look);
    }

    static void PaintSplit(Grid bar, SplitLook look)
    {
        if (bar.Children.Count == 0 || bar.Children[0] is not Border mark)
            return;
        mark.Height = look == SplitLook.Rest ? 1 : 2;
        mark.Background = look switch
        {
            SplitLook.Drag => ThemeBrush("AccentFillColorDefaultBrush", SplitLineBrush),
            SplitLook.Hover => ThemeBrush("AccentFillColorSecondaryBrush", SplitLineBrush),
            _ => SplitLineBrush,
        };
    }

    static Brush ThemeBrush(string key, Brush fallback)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Brush brush)
            return brush;
        return fallback;
    }

    static Grid MakeSplitBar()
    {
        var mark = new Border
        {
            Height = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Background = SplitLineBrush,
            IsHitTestVisible = false,
            UseLayoutRounding = true,
        };
        var bar = new Grid
        {
            Height = SplitThickness,
            Background = BrushOf(0, 0, 0, 0),
            IsHitTestVisible = false,
            UseLayoutRounding = true,
            Children = { mark },
        };
        Canvas.SetZIndex(bar, 40);
        return bar;
    }

    static double ClampRange(double value, double lo, double hi)
    {
        if (hi < lo)
            return lo;
        return Math.Clamp(value, lo, hi);
    }

    int HitGroup(double x)
    {
        var best = -1;
        var bestDist = HitPx;
        for (var i = 0; i < _groups.Count; i++)
        {
            if (IsQuietBoundary(i))
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

    bool IsQuietBoundary(int group)
    {
        if (_file == null || group < 0 || group >= _groups.Count)
            return false;
        var members = _groups[group];
        if (members.Count == 0)
            return false;
        var spans = _file.Spans;
        foreach (var (index, _) in members)
        {
            if (index < 0 || index >= spans.Count || !IsQuietSpan(spans[index].Label))
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

    bool HasLoudness => _loudness.Length >= 2;

    int BandCount => HasLoudness ? 4 : 3;

    double BandArea
    {
        get
        {
            var handles = HasLoudness ? 3 : 2;
            return Math.Max(0, ContentBottom - handles * SplitThickness);
        }
    }

    double ShareFloor
    {
        get
        {
            var area = BandArea;
            if (area < 1)
                return 0.08;
            var byPx = MinBand / area;
            var byCount = 0.92 / BandCount;
            return Math.Min(Math.Min(0.2, byPx), byCount);
        }
    }

    double ShownOtoShare
    {
        get
        {
            var min = ShareFloor;
            return Math.Clamp(_otoShare, min, 1 - (BandCount - 1) * min);
        }
    }

    double ShownLoudShare
    {
        get
        {
            if (!HasLoudness)
                return 0;
            var min = ShareFloor;
            var max = 1 - ShownOtoShare - 2 * min;
            return Math.Clamp(_loudShare, min, Math.Max(min, max));
        }
    }

    double ShownWaveShare
    {
        get
        {
            var min = ShareFloor;
            var max = 1 - ShownOtoShare - ShownLoudShare - min;
            return Math.Clamp(_waveShare, min, Math.Max(min, max));
        }
    }

    double WaveHeight => Math.Max(1, BandArea * ShownWaveShare);

    double OtoHeight => Math.Max(1, BandArea * ShownOtoShare);

    double LoudnessHeight => HasLoudness ? Math.Max(1, BandArea * ShownLoudShare) : 0;

    double SpecHeight => Math.Max(1, BandArea - WaveHeight - LoudnessHeight - OtoHeight);

    double SpecTop => WaveHeight + SplitThickness;

    double LoudnessTop => SpecTop + SpecHeight + (HasLoudness ? SplitThickness : 0);

    double OtoTop =>
        (HasLoudness ? LoudnessTop + LoudnessHeight : SpecTop + SpecHeight) + SplitThickness;

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

    double OtoY(double sample)
    {
        var mid = OtoTop + OtoHeight * 0.5;
        var amp = Math.Max(1, OtoHeight * 0.5 - 6);
        var peak = _wavePeak > 1e-9 ? _wavePeak : 0.2;
        return mid - sample / peak * amp;
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

    static bool IsQuietSpan(string label) => label is "無音" or "息";

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

    void AddOtoVisuals()
    {
        if (_file == null)
            return;

        _otoWave = new Polygon
        {
            Fill = WaveBrush,
            IsHitTestVisible = false,
        };
        _plot.Children.Add(_otoWave);
        _otoWaveLine = new Polyline
        {
            Stroke = WaveLineBrush,
            StrokeThickness = 1.25,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
        _plot.Children.Add(_otoWaveLine);
        _otoZero = new Line
        {
            Stroke = ZeroBrush,
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        _plot.Children.Add(_otoZero);

        OtoMarks.Fill(_file, _oto, _otoMarks, _loudness);
        foreach (var _ in _otoMarks)
            AddOtoMarkVisual();

        _otoCaption = new Border
        {
            Background = OtoChipBrush,
            Padding = new Thickness(4, 0, 4, 0),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "oto.ini",
                FontSize = 11,
                Foreground = TickBrush,
            },
        };
        _plot.Children.Add(_otoCaption);
        LiftOtoChrome();
    }

    void EnsureOtoVisuals(int count)
    {
        if (count <= _otoRects.Count)
            return;
        while (_otoRects.Count < count)
            AddOtoMarkVisual();
        LiftOtoChrome();
    }

    void AddOtoMarkVisual()
    {
        var rect = new Rectangle
        {
            Fill = OtoRegionBrush,
            IsHitTestVisible = false,
        };
        var text = new TextBlock
        {
            Foreground = TextBrush,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false,
        };
        _otoRects.Add(rect);
        _otoTexts.Add(text);
        _plot.Children.Add(rect);
        for (var point = 0; point < OtoPointCount; point++)
        {
            var line = new Line
            {
                Stroke = OtoBrush(point),
                StrokeThickness = 2,
                IsHitTestVisible = false,
            };
            var name = new TextBlock
            {
                Text = OtoPointName(point),
                Foreground = OtoBrush(point),
                FontSize = 10,
                IsHitTestVisible = false,
            };
            var chip = new Border
            {
                Background = OtoChipBrush,
                Padding = new Thickness(3, 0, 3, 0),
                IsHitTestVisible = false,
                Child = name,
            };
            _otoLines.Add(line);
            _otoPointChips.Add(chip);
            _plot.Children.Add(line);
        }

        _plot.Children.Add(text);
    }

    void LiftOtoChrome()
    {
        foreach (var chip in _otoPointChips)
        {
            _plot.Children.Remove(chip);
            _plot.Children.Add(chip);
        }

        if (_otoCaption == null)
            return;
        _plot.Children.Remove(_otoCaption);
        _plot.Children.Add(_otoCaption);
    }

    void ArrangeOtoLabels()
    {
        if (_file == null)
            return;
        OtoMarks.Fill(_file, _oto, _otoMarks, _loudness);
        EnsureOtoVisuals(_otoMarks.Count);
        var count = Math.Min(_otoMarks.Count, _otoRects.Count);
        if (_focusOto >= count)
            _focusOto = -1;
        var lanes = LaneOf(_otoMarks, count);
        var laneCount = 1;
        for (var i = 0; i < count; i++)
            laneCount = Math.Max(laneCount, lanes[i] + 1);

        var usable = Math.Max(0, OtoHeight - 8);
        var pitch = laneCount <= 1 ? 16 : Math.Min(16, usable / laneCount);
        var top = OtoTop;
        var height = OtoHeight;
        for (var i = 0; i < count; i++)
        {
            var mark = _otoMarks[i];
            var start = Math.Min(mark.Offset, mark.Blank);
            var end = Math.Max(mark.Offset, mark.Blank);
            var left = X(start);
            var width = Math.Max(0, X(end) - left);
            var focused = _focusOto >= 0;
            var chosen = i == _focusOto;
            var dim = focused && !chosen;
            var rect = _otoRects[i];
            rect.Fill = chosen || (!focused && i == _hotOto) ? OtoRegionHotBrush : OtoRegionBrush;
            rect.Opacity = dim ? OtoDimOpacity : 1;
            rect.Visibility = width < 1 || height < 2 ? Visibility.Collapsed : Visibility.Visible;
            Canvas.SetLeft(rect, left);
            Canvas.SetTop(rect, top);
            rect.Width = width;
            rect.Height = Math.Max(0, height);

            var textTop = top + 4 + lanes[i] * pitch;
            var text = _otoTexts[i];
            text.Text = mark.Alias;
            text.Width = width;
            text.Opacity = dim ? OtoDimOpacity : 1;
            text.FontWeight = chosen
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal;
            var aliasWide = chosen ? width >= 12 : width >= 28;
            var showAlias = aliasWide && height >= 20 && textTop + 14 <= top + height - 2;
            text.Visibility = showAlias ? Visibility.Visible : Visibility.Collapsed;
            Canvas.SetLeft(text, left);
            Canvas.SetTop(text, textTop);

            for (var point = 0; point < OtoPointCount; point++)
            {
                var line = _otoLines[i * OtoPointCount + point];
                var x = X(mark.At(point));
                line.Opacity = dim ? OtoDimOpacity : 1;
                line.StrokeThickness = chosen ? 3 : 2;
                line.Visibility = height > 1 ? Visibility.Visible : Visibility.Collapsed;
                line.X1 = x;
                line.X2 = x;
                line.Y1 = top;
                line.Y2 = top + height;
            }
        }

        for (var i = count; i < _otoRects.Count; i++)
            _otoRects[i].Visibility = Visibility.Collapsed;
        for (var i = count; i < _otoTexts.Count; i++)
            _otoTexts[i].Visibility = Visibility.Collapsed;
        for (var i = count * OtoPointCount; i < _otoLines.Count; i++)
            _otoLines[i].Visibility = Visibility.Collapsed;

        var textBottom = top + 4 + laneCount * Math.Max(pitch, 14);
        var nameY = top + height - 16;
        var showNames = count > 0 && height >= 56 && nameY >= textBottom;
        if (!showNames)
        {
            foreach (var chip in _otoPointChips)
                chip.Visibility = Visibility.Collapsed;
        }
        else
        {
            PlaceOtoNames(count, nameY);
        }

        if (_otoCaption != null)
        {
            var showCaption = height > 22;
            _otoCaption.Visibility = showCaption ? Visibility.Visible : Visibility.Collapsed;
            Canvas.SetLeft(_otoCaption, Pad + 2);
            Canvas.SetTop(_otoCaption, top + 3);
        }

        if (_focusOto >= 0)
            RaiseFocusedOto();
    }

    void RaiseFocusedOto()
    {
        if (_focusOto < 0 || _focusOto >= _otoRects.Count)
            return;
        Raise(_otoRects[_focusOto]);
        for (var point = 0; point < OtoPointCount; point++)
            Raise(_otoLines[_focusOto * OtoPointCount + point]);
        if (_focusOto < _otoTexts.Count)
            Raise(_otoTexts[_focusOto]);
        foreach (var chip in _otoPointChips)
            Raise(chip);
        var start = _focusOto * OtoPointCount;
        for (var point = 0; point < OtoPointCount; point++)
        {
            if (start + point < _otoPointChips.Count)
                Raise(_otoPointChips[start + point]);
        }

        if (_otoCaption != null)
            Raise(_otoCaption);
        if (_loudnessLabel != null)
            Raise(_loudnessLabel);
        Raise(_waveSplit);
        Raise(_loudSplit);
        Raise(_otoSplit);
    }

    void Raise(UIElement element)
    {
        var children = _plot.Children;
        var index = children.IndexOf(element);
        if (index < 0)
            return;
        var last = children.Count - 1;
        if (index >= last)
            return;
        children.Move((uint)index, (uint)last);
    }

    void PlaceOtoNames(int count, double nameY)
    {
        var pending = new List<(double X, int Index)>();
        for (var i = 0; i < count; i++)
        {
            for (var point = 0; point < OtoPointCount; point++)
            {
                var index = i * OtoPointCount + point;
                var x = X(_otoMarks[i].At(point));
                if (x < -30 || x > _plot.Width + 30)
                {
                    _otoPointChips[index].Visibility = Visibility.Collapsed;
                    continue;
                }

                pending.Add((x, index));
            }
        }

        var focused = _focusOto >= 0 && _focusOto < count;
        pending.Sort((a, b) =>
        {
            if (focused)
            {
                var rank = (a.Index / OtoPointCount == _focusOto ? 0 : 1)
                    .CompareTo(b.Index / OtoPointCount == _focusOto ? 0 : 1);
                if (rank != 0)
                    return rank;
            }

            return a.X.CompareTo(b.X);
        });
        var occupied = new List<(double Left, double Right)>();
        var shown = new bool[_otoPointChips.Count];
        foreach (var item in pending)
        {
            var chip = _otoPointChips[item.Index];
            if (chip.Child is not TextBlock text)
            {
                chip.Visibility = Visibility.Collapsed;
                continue;
            }

            var width = Math.Max(12, text.Text.Length * 10 + 8);
            var left = item.X - width / 2;
            var right = left + width;
            var blocked = false;
            foreach (var box in occupied)
            {
                if (left < box.Right && right > box.Left)
                {
                    blocked = true;
                    break;
                }
            }

            if (blocked)
            {
                chip.Visibility = Visibility.Collapsed;
                continue;
            }

            var dim = focused && item.Index / OtoPointCount != _focusOto;
            chip.Opacity = dim ? OtoDimOpacity : 1;
            chip.Visibility = Visibility.Visible;
            Canvas.SetLeft(chip, left);
            Canvas.SetTop(chip, nameY);
            shown[item.Index] = true;
            occupied.Add((left, right + 4));
        }

        for (var i = 0; i < _otoPointChips.Count; i++)
        {
            if (!shown[i])
                _otoPointChips[i].Visibility = Visibility.Collapsed;
        }
    }

    void ShowOtoHover(double x)
    {
        if (_otoMarks.Count == 0)
        {
            SetHotOto(-1);
            SetReadout("");
            return;
        }

        var best = -1;
        var bestPoint = -1;
        var bestDist = HitPx;
        for (var i = 0; i < _otoMarks.Count; i++)
        {
            var mark = _otoMarks[i];
            for (var point = 0; point < OtoPointCount; point++)
            {
                var dist = Math.Abs(X(mark.At(point)) - x);
                if (dist > bestDist)
                    continue;
                bestDist = dist;
                best = i;
                bestPoint = point;
            }
        }

        if (best >= 0 && bestPoint >= 0)
        {
            var mark = _otoMarks[best];
            var at = mark.At(bestPoint);
            SetHotOto(best);
            SetReadout($"{mark.Alias}   {JoinedPointNames(mark, at)}   {at:0.000} 秒   クリックで強調");
            return;
        }

        var region = HitOtoRegion(XToTime(x));
        SetHotOto(region);
        if (region < 0)
        {
            SetReadout("");
            return;
        }

        var hit = _otoMarks[region];
        SetReadout($"{hit.Alias}   {hit.Offset:0.000}–{hit.Blank:0.000}   クリックで強調");
    }

    void FocusOtoAt(double x)
    {
        var hits = OtoHitsAt(x);
        int next;
        if (hits.Count == 0 || (hits.Count == 1 && hits[0] == _focusOto))
            next = -1;
        else if (hits.Contains(_focusOto))
            next = hits[(hits.IndexOf(_focusOto) + 1) % hits.Count];
        else
            next = BestOtoHit(hits, x);

        SetFocusOto(next);
        if (_focusOto >= 0)
            ShowFocusedOto(x);
        else
            ShowOtoHover(x);
    }

    void SetFocusOto(int index)
    {
        if (index >= _otoMarks.Count)
            index = -1;
        if (_focusOto == index)
            return;
        _focusOto = index;
        _hotOto = -1;
        if (_file != null && _plot.Width >= 2 && _plot.Height >= 2)
            ArrangeOtoLabels();
    }

    void ShowFocusedOto(double x)
    {
        if (_focusOto < 0 || _focusOto >= _otoMarks.Count)
        {
            SetReadout("");
            return;
        }

        var mark = _otoMarks[_focusOto];
        var text = OtoReadout(mark, x);
        if (double.IsNaN(x))
        {
            SetReadout(text);
            return;
        }

        var hits = OtoHitsAt(x);
        if (hits.Count == 0 || (hits.Count == 1 && hits[0] == _focusOto))
        {
            SetReadout(text);
            return;
        }

        var next = hits.Contains(_focusOto)
            ? hits[(hits.IndexOf(_focusOto) + 1) % hits.Count]
            : BestOtoHit(hits, x);
        if (next < 0 || next == _focusOto)
        {
            SetReadout(text);
            return;
        }

        SetReadout(text + "   クリックで " + _otoMarks[next].Alias);
    }

    string OtoReadout(OtoMark mark, double x)
    {
        if (!double.IsNaN(x))
        {
            var bestPoint = -1;
            var bestDist = HitPx;
            for (var point = 0; point < OtoPointCount; point++)
            {
                var dist = Math.Abs(X(mark.At(point)) - x);
                if (dist > bestDist)
                    continue;
                bestDist = dist;
                bestPoint = point;
            }

            if (bestPoint >= 0)
            {
                var at = mark.At(bestPoint);
                return $"{mark.Alias}   {JoinedPointNames(mark, at)}   {at:0.000} 秒";
            }
        }

        return $"{mark.Alias}   {mark.Offset:0.000}–{mark.Blank:0.000}";
    }

    static string JoinedPointNames(OtoMark mark, double at)
    {
        var names = "";
        for (var point = 0; point < OtoPointCount; point++)
        {
            if (Math.Abs(mark.At(point) - at) > 0.0005)
                continue;
            names = names.Length == 0 ? OtoPointName(point) : names + "・" + OtoPointName(point);
        }

        return names;
    }

    List<int> OtoHitsAt(double x)
    {
        var time = XToTime(x);
        var hits = new List<int>();
        for (var i = 0; i < _otoMarks.Count; i++)
        {
            if (OtoContains(_otoMarks[i], time) || OtoPointNear(_otoMarks[i], x))
                hits.Add(i);
        }

        hits.Sort((a, b) =>
        {
            var cmp = _otoMarks[a].Offset.CompareTo(_otoMarks[b].Offset);
            return cmp != 0 ? cmp : a.CompareTo(b);
        });
        return hits;
    }

    int BestOtoHit(List<int> hits, double x)
    {
        var best = hits[0];
        var bestDist = HitPx;
        var foundLine = false;
        foreach (var i in hits)
        {
            var mark = _otoMarks[i];
            for (var point = 0; point < OtoPointCount; point++)
            {
                var dist = Math.Abs(X(mark.At(point)) - x);
                if (dist > bestDist)
                    continue;
                bestDist = dist;
                best = i;
                foundLine = true;
            }
        }

        if (foundLine)
            return best;

        var bestWidth = double.MaxValue;
        foreach (var i in hits)
        {
            var width = Math.Abs(_otoMarks[i].Blank - _otoMarks[i].Offset);
            if (width >= bestWidth)
                continue;
            bestWidth = width;
            best = i;
        }

        return best;
    }

    static bool OtoContains(OtoMark mark, double time)
    {
        var start = Math.Min(mark.Offset, mark.Blank);
        var end = Math.Max(mark.Offset, mark.Blank);
        return time >= start - 1e-9 && time <= end + 1e-9;
    }

    bool OtoPointNear(OtoMark mark, double x)
    {
        for (var point = 0; point < OtoPointCount; point++)
        {
            if (Math.Abs(X(mark.At(point)) - x) <= HitPx)
                return true;
        }

        return false;
    }

    int HitOtoRegion(double time)
    {
        var best = -1;
        var bestWidth = double.MaxValue;
        for (var i = 0; i < _otoMarks.Count; i++)
        {
            var start = Math.Min(_otoMarks[i].Offset, _otoMarks[i].Blank);
            var end = Math.Max(_otoMarks[i].Offset, _otoMarks[i].Blank);
            if (time < start - 1e-9 || time > end + 1e-9)
                continue;
            var width = end - start;
            if (width < bestWidth)
            {
                bestWidth = width;
                best = i;
            }
        }

        return best;
    }

    void SetHotOto(int index)
    {
        if (_focusOto >= 0)
            return;
        if (index >= _otoMarks.Count)
            index = -1;
        if (_hotOto == index)
            return;
        PaintOtoRegion(_hotOto, hot: false);
        _hotOto = index;
        PaintOtoRegion(index, hot: true);
    }

    void PaintOtoRegion(int index, bool hot)
    {
        if (index < 0 || index >= _otoRects.Count)
            return;
        _otoRects[index].Fill = hot ? OtoRegionHotBrush : OtoRegionBrush;
    }

    bool InOtoBand(double y) => _file != null && y >= OtoTop && y < OtoTop + OtoHeight;

    static int[] LaneOf(List<OtoMark> marks, int count)
    {
        var lanes = new int[count];
        var ends = new List<double>();
        for (var i = 0; i < count; i++)
        {
            var start = Math.Min(marks[i].Offset, marks[i].Blank);
            var end = Math.Max(marks[i].Offset, marks[i].Blank);
            var lane = ends.FindIndex(edge => start >= edge - 1e-4);
            if (lane < 0)
            {
                lane = ends.Count;
                ends.Add(end);
            }
            else
            {
                ends[lane] = end;
            }

            lanes[i] = lane;
        }

        return lanes;
    }

    static string OtoPointName(int point) => point switch
    {
        0 => "オフセット",
        1 => "オーバーラップ",
        2 => "先行発声",
        3 => "子音部",
        _ => "ブランク",
    };

    static SolidColorBrush OtoBrush(int point) =>
        OtoPointBrushes[Math.Clamp(point, 0, OtoPointBrushes.Length - 1)];

    sealed class CursorCanvas : Canvas
    {
        public InputCursor Cursor
        {
            get => ProtectedCursor;
            set => ProtectedCursor = value;
        }
    }
}
