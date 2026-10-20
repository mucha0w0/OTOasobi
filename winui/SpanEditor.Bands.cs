using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Otoasobi;

enum BandKind
{
    Wave,
    Spec,
    Loud,
    Oto,
}

public sealed partial class SpanEditor
{
    public UIElement WaveSurface => _waveView;

    public UIElement SpecSurface => _specView;

    public UIElement LoudSurface => _loudView;

    public UIElement OtoSurface => _otoView;

    public bool ZoomFromPointer(PointerRoutedEventArgs e)
    {
        if (_file == null)
            return false;
        var delta = 0;
        foreach (var band in AllBands)
        {
            if (band.ActualWidth < 2 || band.ActualHeight < 2)
                continue;
            var point = e.GetCurrentPoint(band);
            if (point.Position.X < 0 || point.Position.Y < 0
                || point.Position.X > band.ActualWidth
                || point.Position.Y > band.ActualHeight)
                continue;
            UseBand(band);
            delta = point.Properties.MouseWheelDelta;
            if (delta == 0)
                delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
            return ZoomAt(point.Position.X, delta, e, ShiftHeld(e));
        }

        return false;
    }

    BandView[] AllBands => [_waveView, _specView, _loudView, _otoView];

    void BindBands()
    {
        foreach (var band in AllBands)
        {
            band.Sized += (_, _) =>
            {
                if (!band.ColumnDragging)
                    ArrangeAll();
            };
            band.Plot.PointerPressed += OnPointerPressed;
            band.Plot.PointerMoved += OnPointerMoved;
            band.Plot.PointerReleased += OnPointerReleased;
            band.Plot.PointerCanceled += OnPointerReleased;
            band.Plot.PointerCaptureLost += OnPointerCaptureLost;
            band.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnBandWheel), true);
            band.PointerEntered += (_, _) =>
            {
                UseBand(band);
                band.Focus(FocusState.Pointer);
            };
            band.PointerExited += (_, _) => OnBandExited(band);
            band.DoubleTapped += (_, e) => OnBandDoubleTapped(band, e);
            band.KeyDown += (_, e) => OnKeyDown(band, e);
        }

        IsEnabledChanged += (_, _) =>
        {
            foreach (var band in AllBands)
                band.IsEnabled = IsEnabled;
        };
        _otoView.ChildrenOverlay.Children.Add(_labelGate);
    }

    void UseBand(BandView band)
    {
        _xBand = band;
        _hitKind = band.Kind;
    }

    bool UseBand(object sender)
    {
        if (sender is BandView band)
        {
            UseBand(band);
            return true;
        }

        if (sender is not CursorCanvas canvas)
            return false;
        foreach (var view in AllBands)
        {
            if (!ReferenceEquals(view.Plot, canvas))
                continue;
            UseBand(view);
            return true;
        }

        return false;
    }

    void OnBandWheel(object sender, PointerRoutedEventArgs e)
    {
        if (!UseBand(sender))
            return;
        var point = e.GetCurrentPoint(_plot);
        if (ZoomAt(point.Position.X, point.Properties.MouseWheelDelta, e, ShiftHeld(e)))
            e.Handled = true;
    }

    void OnBandExited(BandView band)
    {
        if (_drag != DragKind.None)
            return;
        if (!ReferenceEquals(_xBand, band))
            return;
        SetHotOto(-1);
        if (_focusOto >= 0)
            ShowFocusedOto(double.NaN);
        else
            SetReadout("");
    }

    void OnBandDoubleTapped(BandView band, DoubleTappedRoutedEventArgs e)
    {
        UseBand(band);
        if (_file == null || band.Kind == BandKind.Oto)
        {
            e.Handled = true;
            return;
        }

        FinishDrag();
        SetFullView();
        ArrangeAll();
        e.Handled = true;
    }

    sealed class BandView : UserControl
    {
        const double ResizePreviewOpacity = 0.55;

        readonly Grid _root = new();
        readonly RectangleGeometry _clip = new();
        readonly List<(UIElement Element, Visibility Visibility)> _parkedText = [];
        ScaleTransform? _scale;
        bool _columnDrag;
        bool _resizePreview;
        double _dragWidth = 1;
        double _dragHeight = 1;
        double _opacityBeforePreview = 1;

        public BandView(BandKind kind)
        {
            Kind = kind;
            Plot = new CursorCanvas
            {
                Background = BrushOf(255, 22, 22, 22),
                ManipulationMode = ManipulationModes.None,
                Clip = _clip,
            };
            Readout = new TextBlock
            {
                IsHitTestVisible = false,
                FontSize = 13,
                Foreground = TextBrush,
                TextWrapping = TextWrapping.Wrap,
            };
            Badge = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(12),
                Padding = new Thickness(8, 4, 8, 4),
                Background = BrushOf(190, 22, 22, 22),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
                Child = Readout,
            };
            Notice = new TextBlock
            {
                Text = "音量データがありません",
                Foreground = TickBrush,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };
            ChildrenOverlay = new Grid { IsHitTestVisible = false };
            _root.Children.Add(Plot);
            _root.Children.Add(Notice);
            _root.Children.Add(ChildrenOverlay);
            _root.Children.Add(Badge);
            Content = _root;
            Background = BrushOf(255, 22, 22, 22);
            IsTabStop = true;
            ManipulationMode = ManipulationModes.None;
            SizeChanged += (_, e) =>
            {
                if (_columnDrag)
                {
                    if (_scale != null && _dragWidth > 1 && _dragHeight > 1)
                    {
                        _scale.ScaleX = e.NewSize.Width / _dragWidth;
                        _scale.ScaleY = e.NewSize.Height / _dragHeight;
                    }

                    return;
                }

                if (!SyncPlot(e.NewSize))
                    return;
                Sized?.Invoke(this, EventArgs.Empty);
            };
        }

        public BandKind Kind { get; }

        public CursorCanvas Plot { get; }

        public TextBlock Readout { get; }

        public Border Badge { get; }

        public TextBlock Notice { get; }

        public Grid ChildrenOverlay { get; }

        public event EventHandler? Sized;

        public bool ColumnDragging => _columnDrag;

        public void SetCursor(InputCursor cursor) => ProtectedCursor = cursor;

        public void SetColumnDrag(bool dragging)
        {
            if (dragging)
            {
                if (_columnDrag || ActualWidth < 2 || ActualHeight < 2)
                    return;
                _dragWidth = ActualWidth;
                _dragHeight = ActualHeight;
                _columnDrag = true;
                _scale ??= new ScaleTransform();
                _scale.ScaleX = 1;
                _scale.ScaleY = 1;
                _root.HorizontalAlignment = HorizontalAlignment.Left;
                _root.VerticalAlignment = VerticalAlignment.Top;
                _root.Width = _dragWidth;
                _root.Height = _dragHeight;
                _root.RenderTransformOrigin = new Point(0, 0);
                _root.RenderTransform = _scale;
                BeginResizePreview();
                return;
            }

            if (!_columnDrag)
                return;
            _columnDrag = false;
            _root.RenderTransform = null;
            _root.ClearValue(WidthProperty);
            _root.ClearValue(HeightProperty);
            _root.HorizontalAlignment = HorizontalAlignment.Stretch;
            _root.VerticalAlignment = VerticalAlignment.Stretch;
            SyncPlot(new Size(ActualWidth, ActualHeight));
            EndResizePreview();
        }

        // 拡縮のあいだは文字を隠し、絵だけを薄く見せる。位置の計算は離してから行う。
        void BeginResizePreview()
        {
            if (_resizePreview)
                return;
            _resizePreview = true;
            _opacityBeforePreview = Opacity;
            Opacity = ResizePreviewOpacity;
            foreach (UIElement child in _root.Children)
                ParkTextChrome(child);
        }

        void EndResizePreview()
        {
            if (!_resizePreview)
                return;
            _resizePreview = false;
            Opacity = _opacityBeforePreview;
            foreach (var (element, visibility) in _parkedText)
                element.Visibility = visibility;
            _parkedText.Clear();
        }

        void ParkTextChrome(UIElement element)
        {
            switch (element)
            {
                case TextBlock:
                case Button:
                    Park(element);
                    return;
                case Panel panel:
                    for (var i = 0; i < panel.Children.Count; i++)
                        ParkTextChrome(panel.Children[i]);
                    return;
                case Border border when border.Child is UIElement child && IsTextChrome(child):
                    Park(border);
                    return;
                case Border border when border.Child is UIElement child:
                    ParkTextChrome(child);
                    return;
                case ContentControl control when control.Content is UIElement content && IsTextChrome(content):
                    Park(control);
                    return;
                case ContentControl control when control.Content is UIElement content:
                    ParkTextChrome(content);
                    return;
            }
        }

        static bool IsTextChrome(UIElement element)
        {
            switch (element)
            {
                case TextBlock:
                case Button:
                    return true;
                case Panel panel:
                    if (panel.Children.Count == 0)
                        return false;
                    foreach (UIElement item in panel.Children)
                    {
                        if (!IsTextChrome(item))
                            return false;
                    }

                    return true;
                case Border border:
                    return border.Child is UIElement nested && IsTextChrome(nested);
                case ContentControl control:
                    return control.Content is string
                        || (control.Content is UIElement content && IsTextChrome(content));
                default:
                    return false;
            }
        }

        void Park(UIElement element)
        {
            _parkedText.Add((element, element.Visibility));
            element.Visibility = Visibility.Collapsed;
        }

        bool SyncPlot(Size size)
        {
            // 畳んだ瞬間の 0 や非数で幅を置くと、図形が範囲外で落ちる。
            if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width < 1 || size.Height < 1)
                return false;
            Plot.Width = size.Width;
            Plot.Height = size.Height;
            _clip.Rect = new Rect(0, 0, size.Width, size.Height);
            return true;
        }
    }
}
