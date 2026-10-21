using Microsoft.UI.Input;
using Microsoft.UI.Text;
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
    const double EdgeBand = 44;
    const double EndBand = 16;

    readonly Dictionary<string, PaneChrome> _panes = [];
    readonly Dictionary<string, Rect> _rects = [];
    readonly List<DockRegion> _regions = [];
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
    bool _keepRatios;
    Size _layoutSize;
    SplitSlot? _resize;
    double _resizeRatio;
    Dictionary<LayoutNode, double>? _frozenSplits;
    Dictionary<LayoutNode, PaneBox>? _paneFloors;
    PaneChrome? _pressPane;
    Point _pressPoint;
    bool _moving;
    Pointer? _pointer;
    LayoutNode? _hoverNode;
    DockEdge? _hoverEdge;
    LayoutNode? _dragOrigin;

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

    public bool KeepRatios => _keepRatios;

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

    public void Load(LayoutNode? root, bool keepRatios = false)
    {
        _root = root;
        _keepRatios = keepRatios;
        InvalidateMeasure();
    }

    public void Apply(LayoutNode? root, bool keepRatios)
    {
        _keepRatios = keepRatios;
        SetRoot(root);
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

    public void Reset()
    {
        _keepRatios = false;
        SetRoot(EditorLayout.CreateDefault());
    }

    public void Move(string draggedId, LayoutNode target, DockEdge edge) =>
        SetRoot(EditorLayout.Move(_root, draggedId, target, edge));

    void SetRoot(LayoutNode? root, bool notify = true)
    {
        _root = root;
        InvalidateMeasure();
        if (notify)
            LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? 0 : Math.Max(0, available.Width);
        var height = double.IsInfinity(available.Height) ? 0 : Math.Max(0, available.Height);
        Place(new Size(width, height));
        foreach (var pair in _panes)
        {
            // 0×0 で測ると、一覧やスクロールが範囲外の値で落ちる。無いパネルは並べない。
            if (pair.Value.Visibility != Visibility.Visible)
                continue;
            if (!_rects.TryGetValue(pair.Key, out var rect) || rect.Width < 1 || rect.Height < 1)
                continue;
            pair.Value.Measure(new Size(rect.Width, rect.Height));
        }

        _empty.Measure(new Size(width, height));
        _bars.Measure(new Size(width, height));
        _compass.Measure(new Size(width, height));
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _layoutSize = finalSize;
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
        _regions.Clear();
        _slots.Clear();
        if (_root == null || size.Width < 1 || size.Height < 1)
            return;
        // ドラッグ中は、内側の境目を 160px へ押し戻さない。離したあとも、その比率を保つ。
        if (!_keepRatios && _resize == null)
            EditorLayout.FitDisplayedMinima(_root, size.Width, size.Height);
        Walk(_root, new Rect(0, 0, size.Width, size.Height));
    }

    void Walk(LayoutNode node, Rect rect)
    {
        _regions.Add(new DockRegion(node, rect));
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
        _frozenSplits = _root == null
            ? null
            : EditorLayout.MeasureSplitters(_root, _layoutSize.Width, _layoutSize.Height);
        _paneFloors = _root == null
            ? null
            : EditorLayout.MeasureFloors(_root, _layoutSize.Width, _layoutSize.Height);
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
        if (_resize == null || _root == null || _frozenSplits == null)
            return;
        var proposed = _resize.Horizontal ? point.X : point.Y;
        if (_paneFloors != null && EditorLayout.MoveSplitter(
            _root, _resize.Node, proposed, _frozenSplits, _layoutSize.Width, _layoutSize.Height, _paneFloors))
            InvalidateMeasure();
    }

    void EndResize(GripBar? grip)
    {
        if (_resize == null)
            return;
        var changed = Math.Abs(_resize.Node.Ratio - _resizeRatio) > 0.0005;
        if (changed && _root != null && _layoutSize.Width >= 1 && _layoutSize.Height >= 1
            && EditorLayout.HoldsUndersizedPane(_root, _layoutSize.Width, _layoutSize.Height))
            _keepRatios = true;
        var held = _resize.Grip;
        _resize = null;
        _frozenSplits = null;
        _paneFloors = null;
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
            // 閉じたときと同じ木にする。ヒット判定も、埋めたあとの領域で行う。
            PullDragSource();
        }

        ShowGhost(_pressPane, point);
        var pane = HitPane(point);
        var overSelf = pane == _pressPane.Id;
        Rect? leafRect = pane != null && _rects.TryGetValue(pane, out var found) ? found : null;
        var arm = leafRect is { } armRect && !overSelf ? HitArm(armRect, point) : null;
        if (arm != null && leafRect is { } compassRect)
        {
            var leaf = FindRegion(pane);
            _hoverNode = leaf?.Node;
            _hoverEdge = _hoverNode == null ? null : arm;
            ShowArms(compassRect, _hoverNode == null ? null : Half(compassRect, arm.Value), arm);
            e.Handled = true;
            return;
        }

        if (HitRegionEdge(point, _pressPane.Id) is { } region)
        {
            _hoverNode = region.Node;
            _hoverEdge = region.Edge;
            if (leafRect is { } showRect)
                ShowArms(showRect, Half(region.Rect, region.Edge), null);
            else
                ShowHighlight(Half(region.Rect, region.Edge));
            e.Handled = true;
            return;
        }

        _hoverNode = null;
        _hoverEdge = null;
        if (leafRect is { } idleRect && !overSelf)
            ShowArms(idleRect, null, null);
        else
            HideArms();
        e.Handled = true;
    }

    void OnDockReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pressPane == null)
            return;
        var origin = _dragOrigin;
        var dragged = _pressPane.Id;
        var target = _hoverNode;
        var edge = _hoverEdge;
        var moving = _moving;
        EndTitleDrag();
        if (moving)
            FinishTitleDrag(origin, dragged, target, edge);
        e.Handled = true;
    }

    void OnDockCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_pressPane == null)
            return;
        var origin = _dragOrigin;
        var moving = _moving;
        EndTitleDrag();
        if (moving)
            FinishTitleDrag(origin, null, null, null);
    }

    void EndTitleDrag()
    {
        var pointer = _pointer;
        _pressPane = null;
        _pointer = null;
        _moving = false;
        _dragOrigin = null;
        _hoverNode = null;
        _hoverEdge = null;
        HideArms();
        _ghost.Visibility = Visibility.Collapsed;
        if (pointer == null || PointerCaptures == null)
            return;
        foreach (var captured in PointerCaptures)
        {
            if (captured.PointerId == pointer.PointerId)
            {
                ReleasePointerCapture(pointer);
                break;
            }
        }
    }

    void PullDragSource()
    {
        if (_pressPane == null || _root == null || !EditorLayout.Contains(_root, _pressPane.Id))
            return;
        _dragOrigin = _root;
        _root = EditorLayout.Hide(CloneLayout(_root), _pressPane.Id);
        // ポインタ処理の中で UpdateLayout すると、一覧や図形が範囲外で落ちる。
        _pressPane.Visibility = Visibility.Collapsed;
        InvalidateMeasure();
        if (_layoutSize.Width >= 1 && _layoutSize.Height >= 1)
            Place(_layoutSize);
    }

    // 印か端で離したときだけ新しい木を残す。外したパネルが戻らない結果は、ドラッグ前に戻す。
    void FinishTitleDrag(LayoutNode? origin, string? dragged, LayoutNode? target, DockEdge? edge)
    {
        if (origin == null)
            return;
        LayoutNode? moved = null;
        if (dragged != null && target != null && edge != null)
            moved = EditorLayout.Move(origin, dragged, target, edge.Value);
        if (moved != null && EditorLayout.Contains(moved, dragged!))
        {
            ShowPanes(moved);
            SetRoot(moved);
            return;
        }

        ShowPanes(origin);
        SetRoot(origin, notify: false);
    }

    void ShowPanes(LayoutNode? node)
    {
        if (node == null)
            return;
        if (node.IsLeaf)
        {
            if (node.Id != null && _panes.TryGetValue(node.Id, out var pane))
                pane.Visibility = Visibility.Visible;
            return;
        }

        ShowPanes(node.First);
        ShowPanes(node.Second);
    }

    static LayoutNode CloneLayout(LayoutNode node) => new()
    {
        Id = node.Id,
        Axis = node.Axis,
        Ratio = node.Ratio,
        First = node.First == null ? null : CloneLayout(node.First),
        Second = node.Second == null ? null : CloneLayout(node.Second),
    };

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

    DockRegion? FindRegion(string? id)
    {
        if (id == null)
            return null;
        foreach (var region in _regions)
        {
            if (region.Node.IsLeaf && region.Node.Id == id)
                return region;
        }

        return null;
    }

    // 端の帯では、その端を共有するいちばん大きなまとまりを割る。中央の印は、その1枚だけを割る。
    DockRegion? HitRegionEdge(Point point, string draggedId)
    {
        DockRegion? best = null;
        var bestDistance = double.MaxValue;
        var bestArea = -1.0;
        foreach (var region in _regions)
        {
            if (!SharesEdge(region.Rect, point, out var edge, out var distance))
                continue;
            if (!HasOtherPane(region.Node, draggedId))
                continue;
            var area = region.Rect.Width * region.Rect.Height;
            var nearer = distance < bestDistance - 0.5;
            var sameAndLarger = Math.Abs(distance - bestDistance) <= 0.5 && area > bestArea + 0.5;
            if (!nearer && !sameAndLarger)
                continue;
            region.Edge = edge;
            best = region;
            bestDistance = distance;
            bestArea = area;
        }

        return best;
    }

    static bool HasOtherPane(LayoutNode node, string draggedId)
    {
        if (node.IsLeaf)
            return node.Id != draggedId;
        var first = node.First != null && HasOtherPane(node.First, draggedId);
        var second = node.Second != null && HasOtherPane(node.Second, draggedId);
        return first || second;
    }

    static bool SharesEdge(Rect rect, Point point, out DockEdge edge, out double distance)
    {
        edge = DockEdge.Left;
        distance = double.MaxValue;
        if (point.X < rect.X || point.Y < rect.Y || point.X > rect.Right || point.Y > rect.Bottom)
            return false;
        distance = point.X - rect.X;
        var right = rect.Right - point.X;
        var top = point.Y - rect.Y;
        var bottom = rect.Bottom - point.Y;
        if (right < distance)
        {
            distance = right;
            edge = DockEdge.Right;
        }

        if (top < distance)
        {
            distance = top;
            edge = DockEdge.Top;
        }

        if (bottom < distance)
        {
            distance = bottom;
            edge = DockEdge.Bottom;
        }

        var band = edge is DockEdge.Left or DockEdge.Right ? EdgeBand : EndBand;
        return distance <= band;
    }

    void ShowHighlight(Rect half)
    {
        _highlight.Visibility = Visibility.Visible;
        Canvas.SetLeft(_highlight, half.X);
        Canvas.SetTop(_highlight, half.Y);
        _highlight.Width = Math.Max(0, half.Width);
        _highlight.Height = Math.Max(0, half.Height);
        foreach (var arm in _arms.Values)
            arm.Visibility = Visibility.Collapsed;
    }

    void ShowArms(Rect rect, Rect? highlight, DockEdge? hot)
    {
        if (highlight is { } half)
            ShowHighlight(half);
        else
            _highlight.Visibility = Visibility.Collapsed;

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

    sealed class DockRegion
    {
        public DockRegion(LayoutNode node, Rect rect)
        {
            Node = node;
            Rect = rect;
        }

        public LayoutNode Node { get; }

        public Rect Rect { get; }

        public DockEdge Edge { get; set; }
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
            var bar = new Grid { Height = 20, Padding = new Thickness(8, 0, 0, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock
            {
                Text = title,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            if (Application.Current.Resources.TryGetValue("CaptionTextBlockStyle", out var style) && style is Style textStyle)
                label.Style = textStyle;
            label.FontSize = 12;
            label.FontWeight = FontWeights.SemiBold;
            CloseButton = new Button
            {
                VerticalAlignment = VerticalAlignment.Stretch,
                Content = new FontIcon
                {
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    Glyph = "\uE8BB",
                    FontSize = 10,
                },
            };
            if (Application.Current.Resources.TryGetValue("PaneCloseButtonStyle", out var closeStyle) && closeStyle is Style buttonStyle)
                CloseButton.Style = buttonStyle;
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
