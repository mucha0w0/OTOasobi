using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Otoasobi;

interface INamedPreset
{
    string Name { get; }
    bool BuiltIn { get; }
}

// プリセット名、保存ダイアログ、フライアウトの選択と変更マーク。
static class PresetUi
{
    public static TextBlock ErrorLine()
    {
        var error = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        void Paint()
        {
            if (AppearanceTheme.Brush(AppearanceTheme.Resolve(error), "SystemFillColorCriticalBrush") is Brush paint)
                error.Foreground = paint;
        }

        error.Loaded += (_, _) => Paint();
        error.ActualThemeChanged += (_, _) => Paint();
        Paint();
        return error;
    }

    public static string? BasicNameError(string raw)
    {
        var name = raw.Trim();
        if (name.Length == 0)
            return "名前を入力してください。";
        if (name.Length > 40)
            return "名前は40文字までです。";
        if (name.Any(char.IsControl))
            return "名前に使えない文字があります。";
        return null;
    }

    public static string? ReservedNameError(
        string raw,
        Func<string, bool> reserved,
        string reservedMessage = "その名前は使えません。")
    {
        var basic = BasicNameError(raw);
        if (basic != null)
            return basic;
        if (reserved(raw.Trim()))
            return reservedMessage;
        return null;
    }

    public static async Task<string?> AskNameAsync(
        XamlRoot? root,
        string title,
        string prompt,
        string? currentUserName,
        Func<string, string?> validate,
        string placeholder = "プリセット名")
    {
        if (root == null)
            return null;
        var box = new TextBox { PlaceholderText = placeholder };
        if (!string.IsNullOrEmpty(currentUserName))
        {
            box.Text = currentUserName;
            box.SelectAll();
        }

        var error = ErrorLine();
        var panel = new StackPanel { Spacing = 8, MinWidth = 280 };
        panel.Children.Add(new TextBlock
        {
            Text = prompt,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(box);
        panel.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root,
        };
        dialog.Opened += (_, _) => box.Focus(FocusState.Programmatic);
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var message = validate(box.Text);
            if (message == null)
                return;
            args.Cancel = true;
            error.Text = message;
            error.Visibility = Visibility.Visible;
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;
        return box.Text.Trim();
    }

    public static async Task<bool> ConfirmOverwriteAsync(XamlRoot? root, string title, string content)
    {
        if (root == null)
            return false;
        var confirm = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = "上書きする",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root,
        };
        return await confirm.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task<T?> AcceptSaveAsync<T>(
        XamlRoot? root,
        string title,
        string prompt,
        T? selected,
        IList<T> presets,
        Func<string, string?> validate,
        Func<string, string> overwrite,
        Func<string, T> create,
        Action<T> copy) where T : class, INamedPreset
    {
        var name = await AskNameAsync(
            root,
            title,
            prompt,
            selected is { BuiltIn: false } ? selected.Name : null,
            validate);
        if (name == null)
            return null;

        var existing = presets.FirstOrDefault(preset => preset.Name == name);
        if (existing != null && !ReferenceEquals(existing, selected)
            && !await ConfirmOverwriteAsync(root, "プリセットを上書きしますか？", overwrite(name)))
            return null;

        var target = existing ?? create(name);
        copy(target);
        if (existing == null)
            presets.Add(target);
        return target;
    }

    public static void Fill<T>(
        MenuFlyout flyout,
        string group,
        IEnumerable<T> builtIn,
        IEnumerable<T> user,
        T? selected,
        Action<T> select,
        bool separator) where T : class, INamedPreset
    {
        flyout.Items.Clear();
        foreach (var preset in builtIn)
            Add(flyout, group, preset, selected, select);
        var pendingSeparator = separator;
        foreach (var preset in user)
        {
            if (pendingSeparator)
            {
                flyout.Items.Add(new MenuFlyoutSeparator());
                pendingSeparator = false;
            }

            Add(flyout, group, preset, selected, select);
        }
    }

    public static void MarkDirty<T>(
        MenuFlyout flyout,
        TextBlock? nameText,
        TextBlock dirtyMark,
        T? selected,
        bool dirty,
        string fallbackName) where T : class, INamedPreset
    {
        if (nameText != null)
            nameText.Text = selected?.Name ?? fallbackName;
        dirtyMark.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        foreach (var entry in flyout.Items)
        {
            if (entry is not RadioMenuFlyoutItem radio || radio.Tag is not T preset)
                continue;
            radio.IsChecked = ReferenceEquals(preset, selected);
            var text = dirty && ReferenceEquals(preset, selected) ? preset.Name + " *" : preset.Name;
            if (radio.Text != text)
                radio.Text = text;
        }
    }

    static void Add<T>(
        MenuFlyout flyout,
        string group,
        T preset,
        T? selected,
        Action<T> select) where T : class, INamedPreset
    {
        var item = new RadioMenuFlyoutItem
        {
            Text = preset.Name,
            GroupName = group,
            Tag = preset,
            IsChecked = ReferenceEquals(preset, selected),
        };
        item.Click += (_, _) => select(preset);
        flyout.Items.Add(item);
    }
}
