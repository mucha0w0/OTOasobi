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

public sealed partial class SpanEditor : UserControl
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
        Oto,
    }

    readonly BandView _waveView = new(BandKind.Wave);
    readonly BandView _specView = new(BandKind.Spec);
    readonly BandView _loudView = new(BandKind.Loud);
    readonly BandView _otoView = new(BandKind.Oto);
    readonly Line[] _waveOtoLines = new Line[OtoTune.PointCount];
    readonly Line[] _specOtoLines = new Line[OtoTune.PointCount];
    readonly Line[] _loudOtoLines = new Line[OtoTune.PointCount];
    readonly List<Line> _loudLines = [];
    BandView _xBand = null!;
    BandKind _hitKind;
    CursorCanvas _plot => _xBand.Plot;
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
    static readonly SolidColorBrush TextBrush = BrushOf(255, 249, 250, 251);
    static readonly SolidColorBrush LoudBrush = BrushOf(255, 153, 246, 228);
    static readonly SolidColorBrush LoudBackBrush = BrushOf(255, 14, 14, 14);
    static readonly SolidColorBrush OtoRegionBrush = BrushOf(64, 15, 157, 142);
    static readonly SolidColorBrush OtoRegionHotBrush = BrushOf(120, 45, 212, 191);
    static readonly SolidColorBrush OtoChipBrush = BrushOf(190, 14, 14, 14);
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
    readonly List<Line> _waveLines = [];
    readonly List<TextBlock> _ticks = [];
    readonly List<OtoMark> _otoMarks = [];
    readonly List<OtoMark> _otoAuto = [];
    readonly Dictionary<int, double?[]> _tune = [];
    List<OtoAdjustedEntry>? _pendingTunes;
    List<int>? _tuneDefer;
    readonly List<Rectangle> _otoRects = [];
    readonly List<TextBlock> _otoTexts = [];
    readonly List<Line> _otoLines = [];
    readonly List<Border> _otoPointChips = [];
    List<List<(int Index, bool IsStart)>> _groups = [];
    OtoExport _oto = new(0, 200, 0, new Dictionary<string, double>());
    int _hotOto = -1;
    int _focusOto = -1;
    int _otoDragIndex = -1;
    double _otoPressX;
    bool _otoDragLive;
    readonly bool[] _otoStack = new bool[OtoTune.PointCount];
    readonly double?[] _otoDragOrigin = new double?[OtoTune.PointCount];

    DragKind _drag = DragKind.None;
    int _dragIndex = -1;
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

    // 境目をドラッグしているあいだは文字と値の線を隠し、絵を薄く伸ばす。離したときに一度だけ描き直す。
    public void SetColumnDrag(bool dragging)
    {
        foreach (var band in AllBands)
            band.SetColumnDrag(dragging);
        if (!dragging)
        {
            ArrangeAll();
            PlaceLabelGate();
        }
    }

    public void SetLabelsLocked(bool locked)
    {
        _labelsLocked = locked;
        PlaceLabelGate();
    }

    public SpanEditor()
    {
        _xBand = _waveView;
        _labelGate = MakeLabelGate();
        for (var point = 0; point < OtoTune.PointCount; point++)
        {
            _waveOtoLines[point] = MakeOverlayOtoLine(point);
            _specOtoLines[point] = MakeOverlayOtoLine(point);
            _loudOtoLines[point] = MakeOverlayOtoLine(point);
        }

        Background = BrushOf(255, 22, 22, 22);
        ManipulationMode = ManipulationModes.None;
        IsTabStop = false;
        BindBands();
    }

    void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is BandView band)
            UseBand(band);
        if (_file == null || _drag != DragKind.None)
            return;
        if (e.Key == Windows.System.VirtualKey.Escape && _focusOto >= 0)
        {
            SetFocusOto(-1);
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

    public bool ZoomAt(double x, int delta, object? token = null, bool horizontal = false)
    {
        if (token != null && ReferenceEquals(token, _zoomToken))
            return true;
        if (_file == null || delta == 0 || _drag != DragKind.None || _plot.Width < 2)
            return false;
        _zoomToken = token;
        if (horizontal)
            ScrollByWheel(delta);
        else
            ZoomBy(delta > 0 ? 0.8 : 1.25, x);
        return true;
    }

    // 標準ノッチ（120）で表示幅の 15%。ホイールを上へ回すと先頭側へ動く。
    void ScrollByWheel(int delta) => Nudge(-delta / 120.0 * 0.15);

    internal static bool ShiftHeld(PointerRoutedEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift))
            return true;
        return IsKeyDown(Windows.System.VirtualKey.Shift)
            || IsKeyDown(Windows.System.VirtualKey.LeftShift)
            || IsKeyDown(Windows.System.VirtualKey.RightShift);
    }

    static bool IsKeyDown(Windows.System.VirtualKey key)
    {
        var state = InputKeyboardSource.GetKeyStateForCurrentThread(key);
        return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
    }

    public event EventHandler? Edited;

    internal event EventHandler<OtoAdjustedEntry>? OtoTuned;

    public event EventHandler? ViewportChanged;

    public bool HasContent => _file != null;

    public void Clear()
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
        foreach (var band in AllBands)
            band.Plot.Children.Clear();
        _waveRects.Clear();
        _specRects.Clear();
        _waveTexts.Clear();
        _specTexts.Clear();
        _lines.Clear();
        _waveLines.Clear();
        _ticks.Clear();
        _otoMarks.Clear();
        _otoAuto.Clear();
        _tune.Clear();
        _pendingTunes = null;
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
    }

    internal void SetOtoSettings(OtoExport settings)
    {
        if (!string.Equals(settings.Mode, _oto.Mode, StringComparison.Ordinal))
        {
            // モードが変わると行の意味が変わる。前のモードの直した値は画面から外す。
            _focusOto = -1;
            _tune.Clear();
            _pendingTunes = null;
        }

        _oto = settings;
        if (_file == null || !HasDrawnPlot)
            return;
        ArrangeOtoLabels();
    }

    internal void SetTunes(IReadOnlyList<OtoAdjustedEntry>? entries)
    {
        if (_drag == DragKind.Oto)
            return;
        _tune.Clear();
        _pendingTunes = entries == null ? [] : entries.ToList();
        if (_file == null || !HasDrawnPlot)
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
        foreach (var band in AllBands)
            band.Plot.Children.Clear();
        _dots.Clear();
        _waveRects.Clear();
        _specRects.Clear();
        _waveTexts.Clear();
        _specTexts.Clear();
        _lines.Clear();
        _waveLines.Clear();
        _loudLines.Clear();
        _ticks.Clear();
        _otoMarks.Clear();
        _otoAuto.Clear();
        _tune.Clear();
        _pendingTunes = null;
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
        _specView.Plot.Children.Add(_specImage);

        _wave = new Polygon
        {
            Fill = WaveBrush,
            IsHitTestVisible = false,
        };
        _waveView.Plot.Children.Add(_wave);
        _waveLine = new Polyline
        {
            Stroke = WaveLineBrush,
            StrokeThickness = 1.25,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
        _waveView.Plot.Children.Add(_waveLine);

        _zero = new Line
        {
            Stroke = ZeroBrush,
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        _waveView.Plot.Children.Add(_zero);

        _loudnessBack = new Rectangle
        {
            Fill = LoudBackBrush,
            IsHitTestVisible = false,
        };
        _loudView.Plot.Children.Add(_loudnessBack);

        _loudnessLine = new Polyline
        {
            Stroke = LoudBrush,
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
        _loudView.Plot.Children.Add(_loudnessLine);

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
            _waveView.Plot.Children.Add(waveRect);
            _specView.Plot.Children.Add(specRect);
        }

        _groups = BuildGroups(_file.Spans);
        foreach (var _ in _groups)
        {
            var waveLine = new Line
            {
                Stroke = LineBrush,
                StrokeThickness = 2,
                IsHitTestVisible = false,
            };
            var line = new Line
            {
                Stroke = LineBrush,
                StrokeThickness = 2,
                IsHitTestVisible = false,
            };
            var loudLine = new Line
            {
                Stroke = LineBrush,
                StrokeThickness = 2,
                IsHitTestVisible = false,
            };
            _waveLines.Add(waveLine);
            _lines.Add(line);
            _loudLines.Add(loudLine);
            _waveView.Plot.Children.Add(waveLine);
            _specView.Plot.Children.Add(line);
            _loudView.Plot.Children.Add(loudLine);
        }

        foreach (var text in _waveTexts)
            _waveView.Plot.Children.Add(text);
        foreach (var text in _specTexts)
            _specView.Plot.Children.Add(text);
        foreach (var line in _waveOtoLines)
            _waveView.Plot.Children.Add(line);
        foreach (var line in _specOtoLines)
            _specView.Plot.Children.Add(line);
        UseBand(_otoView);
        AddOtoVisuals();
        _loudView.Plot.Children.Add(_loudnessLabel);
        foreach (var line in _loudOtoLines)
            _loudView.Plot.Children.Add(line);

        ArrangeAll();
    }

    void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!UseBand(sender) || _file == null || _plot.Width < 2)
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

        var oto = HitOtoPoint(x, point.Position.Y);
        if (oto is { } hit)
        {
            if (_focusOto != hit.Index)
                SetFocusOto(hit.Index);
            _drag = DragKind.Oto;
            _otoDragIndex = hit.Index;
            _otoPressX = x;
            _otoDragLive = false;
            ArmOtoStack(hit.Index, hit.Point);
            _changed = false;
            ArmCapture(e.Pointer);
            UseCursor(_cursorEw);
            e.Handled = true;
            return;
        }

        if (InOtoBand(point.Position.Y))
        {
            FocusOtoAt(x);
            e.Handled = true;
            return;
        }

        // 同じ音素をもう一度クリックしても外さない。別の音素の波形・スペクトラム・音量で外す。
        var lockWaveBoundary = WaveBoundaryLocked(point.Position.Y);
        if (ReleaseFocusAt(point.Position.Y, x))
            SetFocusOto(-1);

        if (lockWaveBoundary)
        {
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
        _xBand.SetCursor(cursor);
    }

    void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!UseBand(sender) || _file == null)
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

        if (_drag == DragKind.Oto)
        {
            if (!_otoDragLive)
            {
                if (Math.Abs(x - _otoPressX) < 3)
                {
                    UseCursor(_cursorEw);
                    e.Handled = true;
                    return;
                }

                _otoDragLive = true;
            }

            ApplyOtoDrag(XToTime(x));
            UseCursor(_cursorEw);
            e.Handled = true;
            return;
        }

        var otoHit = HitOtoPoint(x, point.Position.Y);
        if (otoHit is { } hover)
        {
            UseCursor(_cursorEw);
            Highlight(-1);
            if (_focusOto < 0)
                SetHotOto(hover.Index);
            return;
        }

        if (InOtoBand(point.Position.Y))
        {
            UseCursor(_cursorHand);
            Highlight(-1);
            if (_focusOto < 0)
                ShowOtoHover(x);
            return;
        }

        SetHotOto(-1);
        if (WaveBoundaryLocked(point.Position.Y))
        {
            Highlight(-1);
            UseCursor(_cursorArrow);
            return;
        }

        var group = HitGroup(x);
        if (group >= 0)
        {
            UseCursor(_cursorEw);
            Highlight(group);
            return;
        }

        Highlight(-1);
        UseCursor(_cursorArrow);
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
        var otoIndex = _drag == DragKind.Oto && _otoDragLive ? _otoDragIndex : -1;
        _drag = DragKind.None;
        _otoDragIndex = -1;
        _otoDragLive = false;
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

        if (otoIndex >= 0)
        {
            ArrangeOtoLabels();
            if (changed)
                EmitTune(otoIndex);
            FlushDeferredTunes();
            return;
        }

        FlushDeferredTunes();

        if (changed)
            Edited?.Invoke(this, EventArgs.Empty);
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
        if (_file == null)
            return;
        _loudView.Notice.Visibility = HasLoudness ? Visibility.Collapsed : Visibility.Visible;
        ArrangeWave();
        ArrangeSpans();
        foreach (var band in AllBands)
            ArrangeTicks(band);
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
        if (!_labelsLocked || _otoView.Plot.Height < 8)
        {
            _labelGate.Visibility = Visibility.Collapsed;
            _otoView.ChildrenOverlay.IsHitTestVisible = false;
            return;
        }

        var height = Math.Max(0, OtoHeight);
        if (height < 8)
        {
            _labelGate.Visibility = Visibility.Collapsed;
            _otoView.ChildrenOverlay.IsHitTestVisible = false;
            return;
        }

        _labelGate.Margin = new Thickness(0);
        _labelGate.Height = double.NaN;
        _labelGate.VerticalAlignment = VerticalAlignment.Stretch;
        _labelGate.Visibility = Visibility.Visible;
        _otoView.ChildrenOverlay.IsHitTestVisible = true;
    }

    void ArrangeWave()
    {
        UseBand(_specView);
        if (_specImage != null)
        {
            Canvas.SetLeft(_specImage, X(_specT0));
            Canvas.SetTop(_specImage, 0);
            _specImage.Width = Math.Max(1, X(_specT1) - X(_specT0));
            _specImage.Height = Math.Max(1, SpecHeight);
        }

        UseBand(_waveView);
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
        UseBand(_otoView);
        ArrangeOtoWave();
    }

    void ArrangeLoudness()
    {
        UseBand(_loudView);
        var height = LoudnessHeight;
        var top = LoudnessTop;
        var visible = HasLoudness && height > 1;
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
        var waveHeight = WaveHeight;
        var specHeight = SpecHeight;
        var loudHeight = LoudnessHeight;
        var spans = _file.Spans;
        for (var i = 0; i < spans.Count; i++)
        {
            if (IsQuietSpan(spans[i].Label))
            {
                HideSpanChrome(_waveRects, _waveTexts, i);
                HideSpanChrome(_specRects, _specTexts, i);
                continue;
            }

            var waveLeft = XOn(_waveView, spans[i].Start);
            var waveWidth = Math.Max(0, XOn(_waveView, spans[i].End) - waveLeft);
            var specLeft = XOn(_specView, spans[i].Start);
            var specWidth = Math.Max(0, XOn(_specView, spans[i].End) - specLeft);
            PlaceRect(_waveRects, i, waveLeft, 0, waveWidth, waveHeight);
            PlaceRect(_specRects, i, specLeft, 0, specWidth, specHeight);
            PlaceText(_waveTexts, i, waveLeft, 8, waveWidth);
            PlaceText(_specTexts, i, specLeft, 8, specWidth);
        }

        ArrangeOtoLabels();

        for (var i = 0; i < _lines.Count && i < _groups.Count; i++)
        {
            var wave = i < _waveLines.Count ? _waveLines[i] : null;
            var loud = i < _loudLines.Count ? _loudLines[i] : null;
            if (IsQuietBoundary(i))
            {
                _lines[i].Visibility = Visibility.Collapsed;
                if (wave != null)
                    wave.Visibility = Visibility.Collapsed;
                if (loud != null)
                    loud.Visibility = Visibility.Collapsed;
                continue;
            }

            var time = GroupTime(i);
            PlaceBoundary(_lines[i], XOn(_specView, time), specHeight);
            _lines[i].Opacity = _focusOto >= 0 ? OtoDimOpacity : 1;
            if (wave != null)
            {
                PlaceBoundary(wave, XOn(_waveView, time), waveHeight);
                wave.Opacity = _focusOto >= 0 ? OtoDimOpacity : 1;
            }

            if (loud != null)
            {
                PlaceBoundary(loud, XOn(_loudView, time), loudHeight);
                loud.Opacity = _focusOto >= 0 ? OtoDimOpacity : 1;
            }
        }
    }

    static void PlaceBoundary(Line line, double x, double height)
    {
        line.Visibility = Visibility.Visible;
        var at = Finite(x);
        line.X1 = at;
        line.X2 = at;
        line.Y1 = 0;
        line.Y2 = Math.Max(1, Finite(height));
    }

    void ArrangeTicks(BandView band)
    {
        for (var i = _ticks.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(VisualTreeHelper.GetParent(_ticks[i]), band.Plot))
                continue;
            band.Plot.Children.Remove(_ticks[i]);
            _ticks.RemoveAt(i);
        }

        if (band.Plot.Width < 2 || band.Plot.Height < 2)
            return;
        UseBand(band);
        var span = Math.Max(1e-6, _view1 - _view0);
        var step = NiceStep(span / Math.Max(1, PlotWidth / 90));
        var first = Math.Ceiling(_view0 / step) * step;
        var y = band.Plot.Height - Ruler + 1;
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
            band.Plot.Children.Add(tick);
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
            _waveView.Plot.Children.Remove(extra);
        }

        var insertAt = _waveView.Plot.Children.IndexOf(_waveLine) + 1;
        if (insertAt <= 0)
            insertAt = _waveView.Plot.Children.Count;
        while (_dots.Count < points.Count)
        {
            var dot = new Ellipse
            {
                Fill = SampleBrush,
                IsHitTestVisible = false,
            };
            _dots.Add(dot);
            _waveView.Plot.Children.Insert(insertAt + _dots.Count - 1, dot);
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
            _waveView.Plot.Children.Remove(dot);
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

    void Highlight(int group)
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            var stroke = i == group ? HotBrush : LineBrush;
            _lines[i].Stroke = stroke;
            if (i < _waveLines.Count)
                _waveLines[i].Stroke = stroke;
            if (i < _loudLines.Count)
                _loudLines[i].Stroke = stroke;
        }
    }

    void PaintWaveBoundaryOpacity()
    {
        var opacity = _focusOto >= 0 ? OtoDimOpacity : 1;
        foreach (var line in _waveLines)
            line.Opacity = opacity;
        foreach (var line in _lines)
            line.Opacity = opacity;
        foreach (var line in _loudLines)
            line.Opacity = opacity;
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

    bool HasDrawnPlot => AllBands.Any(band => band.Plot.Width >= 2 && band.Plot.Height >= 2);

    bool HasLoudness => _loudness.Length >= 2;

    double ContentHeight(BandView band) => Math.Max(0, band.Plot.Height - Ruler);

    double WaveHeight => Math.Max(1, ContentHeight(_waveView));

    bool InOverlayBand(double y) =>
        _file != null
        && _hitKind is BandKind.Wave or BandKind.Spec or BandKind.Loud
        && y >= 0
        && y < _xBand.Plot.Height;

    // 強調中は、子音・母音境界を掴むと oto.ini の線と取り違える。
    bool WaveBoundaryLocked(double y) =>
        _focusOto >= 0 && _hitKind is BandKind.Wave or BandKind.Spec or BandKind.Loud;

    double OtoHeight => Math.Max(1, ContentHeight(_otoView));

    double LoudnessHeight => HasLoudness ? Math.Max(1, ContentHeight(_loudView)) : 0;

    double SpecHeight => Math.Max(1, ContentHeight(_specView));

    double SpecTop => 0;

    double LoudnessTop => 0;

    double OtoTop => 0;

    double XOn(BandView band, double time)
    {
        var width = Math.Max(1, Finite(band.Plot.Width) - Pad * 2);
        var span = Finite(_view1 - _view0);
        if (span < 1e-9)
            span = 1e-9;
        return Pad + (Finite(time) - Finite(_view0)) / span * width;
    }

    static double Finite(double value)
    {
        return double.IsFinite(value) ? value : 0;
    }

    double X(double time) => XOn(_xBand, time);

    double XToTime(double x)
    {
        return _view0 + (x - Pad) / PlotWidth * (_view1 - _view0);
    }

    double WaveY(double sample)
    {
        var mid = WaveHeight * 0.5;
        var amp = Math.Max(1, mid - 6);
        var peak = _wavePeak > 1e-9 ? _wavePeak : 0.2;
        return Finite(mid - Finite(sample) / peak * amp);
    }

    double LoudnessY(double db)
    {
        var top = LoudnessTop + 5;
        var bottom = LoudnessTop + LoudnessHeight - 3;
        var level = Math.Clamp((Finite(db) - (_loudPeak - LoudnessRangeDb)) / LoudnessRangeDb, 0, 1);
        return Finite(bottom - level * Math.Max(1, bottom - top));
    }

    double OtoY(double sample)
    {
        var mid = OtoTop + OtoHeight * 0.5;
        var amp = Math.Max(1, OtoHeight * 0.5 - 6);
        var peak = _wavePeak > 1e-9 ? _wavePeak : 0.2;
        return Finite(mid - Finite(sample) / peak * amp);
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
        var root = _specView.XamlRoot ?? _waveView.XamlRoot ?? XamlRoot;
        if (root != null && root.RasterizationScale > 0)
            scale = root.RasterizationScale;
        var plot = _specView.Plot.Width >= 2 ? _specView.Plot.Width - Pad * 2 : 0;
        if (plot < 1)
        {
            foreach (var band in AllBands)
            {
                if (band.Plot.Width >= 2)
                    plot = Math.Max(plot, band.Plot.Width - Pad * 2);
            }
        }

        if (plot < 1)
            plot = 1400;

        var spec = _specView.Plot.Height >= 2 ? SpecHeight : 360;
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
        Canvas.SetLeft(rect, Finite(left));
        Canvas.SetTop(rect, Finite(top));
        rect.Width = Math.Max(0, Finite(width));
        rect.Height = Math.Max(0, Finite(height));
    }

    static void PlaceText(List<TextBlock> texts, int index, double left, double top, double width)
    {
        if (index >= texts.Count)
            return;
        var text = texts[index];
        text.Width = Math.Max(0, Finite(width));
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

    static bool IsVowelLabel(string label) =>
        label is "a" or "i" or "u" or "e" or "o" or "N" or "M";

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


    sealed class CursorCanvas : Canvas
    {
        public InputCursor Cursor
        {
            get => ProtectedCursor;
            set => ProtectedCursor = value;
        }
    }
}
