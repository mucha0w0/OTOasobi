using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Otoasobi;

public sealed partial class InfoNote : UserControl
{
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

    void Toggle_Checked(object sender, RoutedEventArgs e)
    {
        Apply();
    }

    void Toggle_Unchecked(object sender, RoutedEventArgs e)
    {
        Apply();
    }

    void Apply()
    {
        if (TitleBlock == null || Body == null || Toggle == null || Head == null || HeaderHost == null || TextColumn == null)
            return;

        var text = Text ?? "";
        var title = Title ?? "";
        var hasHeader = Header != null;
        Body.Text = text;
        Body.TextAlignment = TextAlignment;
        HeaderHost.Content = Header;
        HeaderHost.Visibility = hasHeader ? Visibility.Visible : Visibility.Collapsed;
        TitleBlock.Text = title;
        TitleBlock.Visibility = !hasHeader && title.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (hasHeader && StretchHeader)
        {
            TextColumn.Width = new GridLength(1, GridUnitType.Star);
            Head.HorizontalAlignment = HorizontalAlignment.Stretch;
            HeaderHost.HorizontalAlignment = HorizontalAlignment.Stretch;
            Toggle.VerticalAlignment = VerticalAlignment.Center;
            Toggle.Margin = new Thickness(0);
        }
        else
        {
            TextColumn.Width = GridLength.Auto;
            Head.HorizontalAlignment = TextAlignment == TextAlignment.Center
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left;
            HeaderHost.HorizontalAlignment = HorizontalAlignment.Left;
            Toggle.VerticalAlignment = hasHeader ? VerticalAlignment.Top : VerticalAlignment.Center;
            Toggle.Margin = hasHeader ? new Thickness(0, 4, 0, 0) : new Thickness(0);
        }

        var hasText = !string.IsNullOrWhiteSpace(text);
        var open = Toggle.IsChecked == true && hasText;
        Toggle.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        Body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (!hasText)
            Toggle.IsChecked = false;

        var label = string.IsNullOrEmpty(Label) ? title : Label;
        AutomationProperties.SetName(Toggle, string.IsNullOrEmpty(label) ? "説明" : label + "の説明");
        ToolTipService.SetToolTip(Toggle, open ? "説明を閉じる" : "説明");
    }
}
