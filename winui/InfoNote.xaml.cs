using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Otoasobi;

public sealed partial class InfoNote : UserControl
{
    const double TailWidth = 10;

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(InfoNote),
        new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(InfoNote),
        new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label),
        typeof(string),
        typeof(InfoNote),
        new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header),
        typeof(object),
        typeof(InfoNote),
        new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(
        nameof(TextAlignment),
        typeof(TextAlignment),
        typeof(InfoNote),
        new PropertyMetadata(TextAlignment.Left, OnChanged));

    public static readonly DependencyProperty StretchHeaderProperty = DependencyProperty.Register(
        nameof(StretchHeader),
        typeof(bool),
        typeof(InfoNote),
        new PropertyMetadata(false, OnChanged));

    public InfoNote()
    {
        InitializeComponent();
        Apply();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public bool StretchHeader
    {
        get => (bool)GetValue(StretchHeaderProperty);
        set => SetValue(StretchHeaderProperty, value);
    }

    static void OnChanged(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        if (source is InfoNote note)
            note.Apply();
    }

    void Apply()
    {
        if (TitleBlock == null || Body == null || InfoButton == null || Head == null || HeaderHost == null || TextColumn == null)
            return;

        var text = Text ?? "";
        var title = Title ?? "";
        var hasHeader = Header != null;
        Body.Text = text;
        HeaderHost.Content = Header;
        HeaderHost.Visibility = hasHeader ? Visibility.Visible : Visibility.Collapsed;
        TitleBlock.Text = title;
        TitleBlock.Visibility = !hasHeader && title.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (hasHeader && StretchHeader)
        {
            TextColumn.Width = new GridLength(1, GridUnitType.Star);
            Head.HorizontalAlignment = HorizontalAlignment.Stretch;
            HeaderHost.HorizontalAlignment = HorizontalAlignment.Stretch;
            InfoButton.VerticalAlignment = VerticalAlignment.Center;
            InfoButton.Margin = new Thickness(0);
        }
        else
        {
            TextColumn.Width = GridLength.Auto;
            Head.HorizontalAlignment = TextAlignment == TextAlignment.Center
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left;
            HeaderHost.HorizontalAlignment = HorizontalAlignment.Left;
            InfoButton.VerticalAlignment = hasHeader ? VerticalAlignment.Top : VerticalAlignment.Center;
            InfoButton.Margin = hasHeader ? new Thickness(0, 8, 0, 0) : new Thickness(0);
        }

        var hasText = !string.IsNullOrWhiteSpace(text);
        InfoButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        var label = string.IsNullOrEmpty(Label) ? title : Label;
        AutomationProperties.SetName(InfoButton, string.IsNullOrEmpty(label) ? "説明" : label + "の説明");
    }

    void Bubble_Opened(object sender, object e)
    {
        Bubble.UpdateLayout();
        if (XamlRoot?.Content is not UIElement root || InfoButton.ActualWidth <= 0 || Bubble.ActualWidth <= 0)
            return;

        var anchor = InfoButton.TransformToVisual(root).TransformPoint(
            new Point(InfoButton.ActualWidth / 2, InfoButton.ActualHeight / 2));
        var origin = Bubble.TransformToVisual(root).TransformPoint(new Point(0, 0));
        var above = origin.Y + Bubble.ActualHeight / 2 < anchor.Y;
        TailUp.Visibility = above ? Visibility.Collapsed : Visibility.Visible;
        TailDown.Visibility = above ? Visibility.Visible : Visibility.Collapsed;

        var left = Math.Clamp(anchor.X - origin.X - TailWidth / 2, 12, Math.Max(12, Bubble.ActualWidth - TailWidth - 12));
        PlaceTail(above ? TailDown : TailUp, left, above);
    }

    static void PlaceTail(Polygon tail, double left, bool pointsDown)
    {
        tail.Margin = pointsDown
            ? new Thickness(left, -1, 0, 0)
            : new Thickness(left, 0, 0, -1);
    }
}
