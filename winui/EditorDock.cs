using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace Otoasobi;

public sealed class EditorDock : Panel
{
    const double GripSlop = 8;

    readonly Dictionary<string, PaneChrome> _panes = [];
    readonly Dictionary<string, Rect> _rects = [];
    readonly List<SplitSlot> _slots = [];
        readonly List<GripBar> _grips = [];
    readonly Canvas _bars = new() { IsHitTestVisible = true };
    readonly Canvas _compass = new() { IsHitTestVisible = false };
    readonly TextBlock _empty;
    readonly Border _highlight;
    readonly Border _ghost;
    readonly TextBlock _ghostText;
    readonly Dictionary<DockEdge, Border> _arms = [];

    LayoutNode? _root;
    SplitSlot? _resize;
    double _resizeRatio;
    PaneChrome? _pressPane;
    Point _pressPoint;
    bool _moving;
    Pointer? _pointer;
    string? _hoverPane;
    DockEdge? _hoverEdge;

    public EditorDock()
    {
        _empty = new TextBlock
        {
            Text = "表示メニューからパネルを開けます",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24),
            Visibility = Visibility.Collapsed,
        };
        if (Application.Current.Resources.TryGetValue("BodyTextBlockStyle", out var body) && body is Style bodyStyle)
            _empty.Style = bodyStyle;
        if (Application.Current.Resources.TryGetValue("TextFillColorSecondaryBrush", out var secondary) && secondary is Brush secondaryBrush)
            _empty.Foreground = secondaryBrush;

        _highlight = new Border
        {
            IsHitTestVisible = false,
            Opacity = 0.28,
            Visibility = Visibility.Collapsed,
            Background = ThemeBrush("AccentFillColorDefaultBrush", Color.FromArgb(255, 15, 157, 142)),
        };
        _ghostText = new TextBlock
        {
            Margin = new Thickness(10, 6, 10, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _ghost = new Border
        {
            Width = 160,
            Height = 72,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeBrush("AccentFillColorDefaultBrush", Color.FromArgb(255, 15, 157, 142)),
            Background = ThemeBrush("LayerFillColorDefaultBrush", Color.FromArgb(230, 32, 32, 32)),
            Child = _ghostText,
        };
        foreach (var edge in new[] { DockEdge.Left, DockEdge.Right, DockEdge.Top, DockEdge.Bottom })
        {
            var arm = new Border
            {
                Width = 28,
                Height = 28,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
                BorderThickness = new Thickness(1),
                BorderBrush = ThemeBrush("DividerStrokeColorDefaultBrush", Color.FromArgb(255, 120, 120, 120)),
                Background = ThemeBrush("CardBackgroundFillColorDefaultBrush", Color.FromArgb(255, 32, 32, 32)),
                Child = new TextBlock
                {
                    Text = EdgeLabel(edge),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12,
                },
            };
            AutomationProperties.SetName(arm, EdgeName(edge));
            _arms[edge] = arm;
            _compass.Children.Add(arm);
        }

        _compass.Children.Add(_highlight);
        _compass.Children.Add(_ghost);
        foreach (var arm in _arms.Values)
        {
            _compass.Children.Remove(arm);
            _compass.Children.Add(arm);
        }

        Children.Add(_empty);
        Children.Add(_bars);
        Children.Add(_compass);
        PointerMoved += OnDockMoved;
        PointerReleased += OnDockReleased;
        PointerCaptureLost += OnDockCaptureLost;
        PointerCanceled += OnDockReleased;
    }

    public LayoutNode? Root => _root;

    public event EventHandler? LayoutChanged;

    public event EventHandler<bool>? PaneResizing;

    public void Mount(IReadOnlyList<(string Id, string Title, UIElement Content)> panes)
    {
        foreach (var (id, title, content) in panes)
        {
            Detach(content);
            var chrome = new PaneChrome(id, title);
            chrome.CloseClicked += (_, _) => Hide(id);
            chrome.TitlePressed += OnTitlePressed;
            chrome.Body.Children.Add(content);
            _panes[id] = chrome;
            Children.Insert(0, chrome);
        }
    }

    public void Load(LayoutNode? root)
    {
        _root = root;
        InvalidateMeasure();
    }

    public bool Contains(string id) => EditorLayout.Contains(_root, id);

    public void Show(string id)
    {
        if (!EditorLayout.PaneIds.Contains(id) || !_panes.ContainsKey(id))
            return;
        var boxes = new Dictionary<string, PaneBox>();
        foreach (var pair in _rects)
            boxes[pair.Key] = new PaneBox(pair.Value.Width, pair.Value.Height);
        SetRoot(EditorLayout.Show(_root, id, boxes));
    }

    public void Hide(string id) => SetRoot(EditorLayout.Hide(_root, id));

    public void Reset() => SetRoot(EditorLayout.CreateDefault());

    public void Move(string draggedId, string targetId, DockEdge edge) =>
        SetRoot(EditorLayout.Move(_root, draggedId, targetId, edge));

    void SetRoot(LayoutNode? root)
    {
        _root = root;
        InvalidateMeasure();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? 0 : Math.Max(0, available.Width);
        var height = double.IsInfinity(available.Height) ? 0 : Math.Max(0, available.Height);
        Place(new Size(width, height));
        foreach (var pair in _panes)
        {
            if (_rects.TryGetValue(pair.Key, out var rect))
                pair.Value.Measure(new Size(Math.Max(0, rect.Width), Math.Max(0, rect.Height)));
            else
                pair.Value.Measure(new Size(0, 0));
        }

        _empty.Measure(new Size(width, height));
        _bars.Measure(new Size(width, height));
        _compass.Measure(new Size(width, height));
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Place(finalSize);
        foreach (var pair in _panes)
        {
            if (_rects.TryGetValue(pair.Key, out var rect))
            {
                pair.Value.Visibility = Visibility.Visible;
                pair.Value.Arrange(rect);
            }
            else
            {
                pair.Value.Visibility = Visibility.Collapsed;
                pair.Value.Arrange(new Rect(0, 0, 0, 0));
            }
        }

        _empty.Visibility = _root == null ? Visibility.Visible : Visibility.Collapsed;
        _empty.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        _bars.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        _compass.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        PlaceGrips();
        return finalSize;
    }

    void Place(Size size)
    {
        _rects.Clear();
        _slots.Clear();
        if (_root == null || size.Width < 1 || size.Height < 1)
            return;
        Walk(_root, new Rect(0, 0, size.Width, size.Height));
    }

    void Walk(LayoutNode node, Rect rect)
    {
        if (node.IsLeaf)
        {
            if (node.Id != null && _panes.ContainsKey(node.Id))
                _rects[node.Id] = rect;
            return;
        }

        if (node.First == null || node.Second == null)
            return;
        var horizontal = node.Axis == "h";
        var total = horizontal ? rect.Width : rect.Height;
        var ratio = EditorLayout.ClampRatio(node.Ratio, total);
        if (horizontal)
        {
            var firstWidth = rect.Width * ratio;
            var first = new Rect(rect.X, rect.Y, firstWidth, rect.Height);
            var second = new Rect(rect.X + firstWidth, rect.Y, Math.Max(0, rect.Width - firstWidth), rect.Height);
            Walk(node.First, first);
            Walk(node.Second, second);
            _slots.Add(new SplitSlot(node, rect, new Rect(rect.X + firstWidth - GripSlop / 2, rect.Y, GripSlop, rect.Height), true));
            return;
        }

        var firstHeight = rect.Height * ratio;
        Walk(node.First, new Rect(rect.X, rect.Y, rect.Width, firstHeight));
        Walk(node.Second, new Rect(rect.X, rect.Y + firstHeight, rect.Width, Math.Max(0, rect.Height - firstHeight)));
        _slots.Add(new SplitSlot(node, rect, new Rect(rect.X, rect.Y + firstHeight - GripSlop / 2, rect.Width, GripSlop), false));
    }

    void PlaceGrips()
    {
        while (_grips.Count < _slots.Count)
        {
            var grip = new GripBar();
            grip.PointerEntered += (_, _) => PaintGrip(grip, true);
            grip.PointerExited += (_, _) =>
            {
                if (!ReferenceEquals(_resize?.Grip, grip))
                    PaintGrip(grip, false);
            };
            grip.PointerPressed += OnGripPressed;
            grip.PointerMoved += OnGripMoved;
            grip.PointerReleased += OnGripReleased;
            grip.PointerCanceled += OnGripReleased;
            grip.PointerCaptureLost += OnGripReleased;
            _grips.Add(grip);
            _bars.Children.Add(grip);
        }

        for (var i = 0; i < _grips.Count; i++)
        {
            var grip = _grips[i];
            if (i >= _slots.Count)
            {
                grip.Visibility = Visibility.Collapsed;
                continue;
            }

            var slot = _slots[i];
            slot.Grip = grip;
            _slots[i] = slot;
            grip.Visibility = Visibility.Visible;
            grip.Tag = i;
            Canvas.SetLeft(grip, slot.Hit.X);
            Canvas.SetTop(grip, slot.Hit.Y);
            grip.Width = Math.Max(1, slot.Hit.Width);
            grip.Height = Math.Max(1, slot.Hit.Height);
            var mark = grip.Mark;
            if (slot.Horizontal)
            {
                mark.Width = 1;
                mark.Height = double.NaN;
                mark.HorizontalAlignment = HorizontalAlignment.Center;
                mark.VerticalAlignment = VerticalAlignment.Stretch;
            }
            else
            {
                mark.Width = double.NaN;
                mark.Height = 1;
                mark.HorizontalAlignment = HorizontalAlignment.Stretch;
                mark.VerticalAlignment = VerticalAlignment.Center;
            }

            grip.SetCursor(InputSystemCursor.Create(
                slot.Horizontal ? InputSystemCursorShape.SizeWestEast : InputSystemCursorShape.SizeNorthSouth));
        }
    }

    void OnGripPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_moving || sender is not GripBar grip || grip.Tag is not int index || index < 0 || index >= _slots.Count)
            return;
        if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
            return;
        var slot = _slots[index];
        _resize = slot;
        _resizeRatio = slot.Node.Ratio;
        PaintGrip(grip, true);
        grip.CapturePointer(e.Pointer);
        PaneResizing?.Invoke(this, true);
        e.Handled = true;
    }

    void OnGripMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_resize == null || sender is not GripBar grip || !ReferenceEquals(_resize.Grip, grip))
            return;
        ApplyGrip(e.GetCurrentPoint(this).Position);
        e.Handled = true;
    }

    void OnGripReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_resize == null)
            return;
        ApplyGrip(e.GetCurrentPoint(this).Position);
        EndResize(sender as GripBar);
        e.Handled = true;
    }

    void ApplyGrip(Point point)
    {
        if (_resize == null)
            return;
        var slot = _resize;
        var total = slot.Horizontal ? slot.Parent.Width : slot.Parent.Height;
        if (total < 1)
            return;
        var along = slot.Horizontal ? point.X - slot.Parent.X : point.Y - slot.Parent.Y;
        var ratio = EditorLayout.ClampRatio(along / total, total);
        if (Math.Abs(ratio - slot.Node.Ratio) < 0.0005)
            return;
        slot.Node.Ratio = ratio;
        InvalidateMeasure();
    }

    void EndResize(GripBar? grip)
    {
        if (_resize == null)
            return;
        var changed = Math.Abs(_resize.Node.Ratio - _resizeRatio) > 0.0005;
        var held = _resize.Grip;
        _resize = null;
        if (held != null)
        {
            held.ReleasePointerCaptures();
            PaintGrip(held, false);
        }
        else if (grip != null)
        {
            grip.ReleasePointerCaptures();
            PaintGrip(grip, false);
        }

        PaneResizing?.Invoke(this, false);
        if (changed)
            LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    static void PaintGrip(GripBar grip, bool hot)
    {
        var mark = grip.Mark;
        mark.Background = hot
            ? ThemeBrush("AccentFillColorDefaultBrush", Color.FromArgb(255, 15, 157, 142))
            : ThemeBrush("DividerStrokeColorDefaultBrush", Color.FromArgb(255, 90, 90, 90));
        if (mark.Width is > 0 and < 8)
            mark.Width = hot ? 2 : 1;
        if (mark.Height is > 0 and < 8)
            mark.Height = hot ? 2 : 1;
    }

    void OnTitlePressed(object? sender, PointerRoutedEventArgs e)
    {
        if (_resize != null || sender is not PaneChrome pane)
            return;
        if (!e.GetCurrentPoint(pane).Properties.IsLeftButtonPressed)
            return;
        if (IsCloseSource(e.OriginalSource as DependencyObject, pane.CloseButton))
            return;
        _pressPane = pane;
        _pressPoint = e.GetCurrentPoint(this).Position;
        _moving = false;
        _pointer = e.Pointer;
        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    void OnDockMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pressPane == null)
            return;
        var point = e.GetCurrentPoint(this).Position;
        if (!_moving)
        {
            var dx = point.X - _pressPoint.X;
            var dy = point.Y - _pressPoint.Y;
            if (dx * dx + dy * dy < 16)
                return;
            _moving = true;
        }

        ShowGhost(_pressPane, point);
        var pane = HitPane(point);
        if (pane == null || pane == _pressPane.Id || !_rects.TryGetValue(pane, out var rect))
        {
            HideArms();
            _hoverPane = null;
            _hoverEdge = null;
            e.Handled = true;
            return;
        }

        var edge = HitArm(rect, point);
        _hoverPane = pane;
        _hoverEdge = edge;
        ShowArms(rect, edge);
        e.Handled = true;
    }

    void OnDockReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pressPane == null)
            return;
        var dragged = _pressPane.Id;
        var target = _hoverPane;
        var edge = _hoverEdge;
        EndTitleDrag();
        if (target != null && edge != null)
            Move(dragged, target, edge.Value);
        e.Handled = true;
    }

    void OnDockCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_pressPane == null)
            return;
        EndTitleDrag();
    }

    void EndTitleDrag()
    {
        if (_pointer != null)
        {
            var pointer = _pointer;
            _pointer = null;
            if (PointerCaptures != null)
            {
                foreach (var captured in PointerCaptures)
                {
                    if (captured.PointerId == pointer.PointerId)
                    {
                        ReleasePointerCapture(pointer);
                        break;
                    }
                }
            }
        }

        _pressPane = null;
        _moving = false;
        _hoverPane = null;
        _hoverEdge = null;
        HideArms();
        _ghost.Visibility = Visibility.Collapsed;
    }

    string? HitPane(Point point)
    {
        foreach (var pair in _rects)
        {
            if (pair.Value.Contains(point))
                return pair.Key;
        }

        return null;
    }

    void ShowGhost(PaneChrome pane, Point point)
    {
        _ghostText.Text = pane.Title;
        _ghost.Visibility = Visibility.Visible;
        Canvas.SetLeft(_ghost, point.X + 14);
        Canvas.SetTop(_ghost, point.Y + 14);
    }

    void ShowArms(Rect rect, DockEdge? hot)
    {
        _highlight.Visibility = hot == null ? Visibility.Collapsed : Visibility.Visible;
        if (hot != null)
        {
            var half = Half(rect, hot.Value);
            Canvas.SetLeft(_highlight, half.X);
            Canvas.SetTop(_highlight, half.Y);
            _highlight.Width = Math.Max(0, half.Width);
            _highlight.Height = Math.Max(0, half.Height);
        }

        foreach (var pair in _arms)
        {
            var arm = ArmRect(rect, pair.Key);
            pair.Value.Visibility = Visibility.Visible;
            Canvas.SetLeft(pair.Value, arm.X);
            Canvas.SetTop(pair.Value, arm.Y);
            pair.Value.BorderBrush = pair.Key == hot
                ? ThemeBrush("AccentFillColorDefaultBrush", Color.FromArgb(255, 15, 157, 142))
                : ThemeBrush("DividerStrokeColorDefaultBrush", Color.FromArgb(255, 120, 120, 120));
        }
    }

    void HideArms()
    {
        _highlight.Visibility = Visibility.Collapsed;
        foreach (var arm in _arms.Values)
            arm.Visibility = Visibility.Collapsed;
    }

    static DockEdge? HitArm(Rect rect, Point point)
    {
        DockEdge? best = null;
        foreach (var edge in new[] { DockEdge.Left, DockEdge.Right, DockEdge.Top, DockEdge.Bottom })
        {
            if (ArmRect(rect, edge).Contains(point))
                best = edge;
        }

        return best;
    }

    static Rect ArmRect(Rect target, DockEdge edge)
    {
        const double size = 28;
        const double gap = 6;
        var cx = target.X + target.Width / 2;
        var cy = target.Y + target.Height / 2;
        return edge switch
        {
            DockEdge.Left => new Rect(cx - gap - size, cy - size / 2, size, size),
            DockEdge.Right => new Rect(cx + gap, cy - size / 2, size, size),
            DockEdge.Top => new Rect(cx - size / 2, cy - gap - size, size, size),
            _ => new Rect(cx - size / 2, cy + gap, size, size),
        };
    }

    static Rect Half(Rect target, DockEdge edge)
    {
        return edge switch
        {
            DockEdge.Left => new Rect(target.X, target.Y, target.Width / 2, target.Height),
            DockEdge.Right => new Rect(target.X + target.Width / 2, target.Y, target.Width / 2, target.Height),
            DockEdge.Top => new Rect(target.X, target.Y, target.Width, target.Height / 2),
            _ => new Rect(target.X, target.Y + target.Height / 2, target.Width, target.Height / 2),
        };
    }

    static string EdgeLabel(DockEdge edge) => edge switch
    {
        DockEdge.Left => "左",
        DockEdge.Right => "右",
        DockEdge.Top => "上",
        _ => "下",
    };

    static string EdgeName(DockEdge edge) => edge switch
    {
        DockEdge.Left => "左へ入れる",
        DockEdge.Right => "右へ入れる",
        DockEdge.Top => "上へ入れる",
        _ => "下へ入れる",
    };

    static bool IsCloseSource(DependencyObject? source, Button close)
    {
        while (source != null)
        {
            if (ReferenceEquals(source, close))
                return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    static void Detach(UIElement element)
    {
        if (element is not FrameworkElement framework)
            return;
        switch (framework.Parent)
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case Border border when ReferenceEquals(border.Child, element):
                border.Child = null;
                break;
            case ContentControl control when ReferenceEquals(control.Content, element):
                control.Content = null;
                break;
        }
    }

    static Brush ThemeBrush(string key, Color fallback)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Brush brush)
            return brush;
        return new SolidColorBrush(fallback);
    }

    sealed class GripBar : Grid
    {
        public GripBar()
        {
            Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255));
            Mark = new Border
            {
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = ThemeBrush("DividerStrokeColorDefaultBrush", Color.FromArgb(255, 90, 90, 90)),
            };
            Children.Add(Mark);
        }

        public Border Mark { get; }

        public void SetCursor(InputCursor cursor) => ProtectedCursor = cursor;
    }

    sealed class SplitSlot
    {
        public SplitSlot(LayoutNode node, Rect parent, Rect hit, bool horizontal)
        {
            Node = node;
            Parent = parent;
            Hit = hit;
            Horizontal = horizontal;
        }

        public LayoutNode Node { get; }

        public Rect Parent { get; }

        public Rect Hit { get; }

        public bool Horizontal { get; }

        public GripBar? Grip { get; set; }
    }

    sealed class PaneChrome : Grid
    {
        public PaneChrome(string id, string title)
        {
            Id = id;
            Title = title;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var bar = new Grid { ColumnSpacing = 8, Padding = new Thickness(12, 6, 8, 6) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock
            {
                Text = title,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            if (Application.Current.Resources.TryGetValue("BodyStrongTextBlockStyle", out var style) && style is Style textStyle)
                label.Style = textStyle;
            CloseButton = new Button
            {
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Content = new SymbolIcon { Symbol = Symbol.Clear },
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(CloseButton, title + "を閉じる");
            ToolTipService.SetToolTip(CloseButton, "閉じます");
            CloseButton.Click += (_, _) => CloseClicked?.Invoke(this, EventArgs.Empty);
            Grid.SetColumn(CloseButton, 1);
            bar.Children.Add(label);
            bar.Children.Add(CloseButton);
            var header = new Border
            {
                BorderBrush = ThemeBrush("DividerStrokeColorDefaultBrush", Color.FromArgb(255, 80, 80, 80)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = ThemeBrush("LayerFillColorDefaultBrush", Color.FromArgb(255, 32, 32, 32)),
                Child = bar,
            };
            header.PointerPressed += (_, e) => TitlePressed?.Invoke(this, e);
            Children.Add(header);
            Body = new Grid();
            Grid.SetRow(Body, 1);
            Children.Add(Body);
            AutomationProperties.SetName(this, title);
        }

        public string Id { get; }

        public string Title { get; }

        public Button CloseButton { get; }

        public Grid Body { get; }

        public event EventHandler? CloseClicked;

        public event EventHandler<PointerRoutedEventArgs>? TitlePressed;
    }
}
