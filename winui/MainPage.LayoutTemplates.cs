using Microsoft.UI.Xaml.Controls;

namespace Otoasobi;

public sealed partial class MainPage
{
    readonly LayoutTemplate _builtInLayout = LayoutTemplate.CreateBuiltIn();
    readonly LayoutTemplate _recordingLayout = LayoutTemplate.CreateRecording();
    readonly List<LayoutTemplate> _layoutTemplates = [];
    LayoutTemplate? _appliedLayoutTemplate;
    bool _applyingLayoutTemplate;

    void LoadLayoutTemplates()
    {
        _layoutTemplates.Clear();
        _layoutTemplates.AddRange(LayoutTemplateStore.Load());
    }

    LayoutTemplate? MatchLayoutTemplate(LayoutNode? root, bool keepRatios)
    {
        foreach (var template in _layoutTemplates)
        {
            if (template.Matches(root, keepRatios))
                return template;
        }

        return _builtInLayout.Matches(root, keepRatios) ? _builtInLayout
            : _recordingLayout.Matches(root, keepRatios) ? _recordingLayout
            : null;
    }

    void ApplyLayoutTemplate(LayoutTemplate template)
    {
        _applyingLayoutTemplate = true;
        _appliedLayoutTemplate = template;
        try
        {
            Dock.Apply(EditorLayout.Clone(template.Root), template.KeepRatios);
        }
        finally
        {
            _applyingLayoutTemplate = false;
        }
    }

    void SyncLayoutTemplateMenu()
    {
        if (!LayoutTemplateMenuMatches())
            RebuildLayoutTemplateMenu();
        foreach (var entry in LayoutTemplateMenu.Items)
        {
            if (entry is not RadioMenuFlyoutItem radio || radio.Tag is not LayoutTemplate template)
                continue;
            radio.IsChecked = ReferenceEquals(template, _appliedLayoutTemplate);
        }

        DeleteLayoutTemplateMenu.IsEnabled = _layoutTemplates.Count > 0;
    }

    bool LayoutTemplateMenuMatches()
    {
        var expected = 2 + (_layoutTemplates.Count == 0 ? 0 : 1) + _layoutTemplates.Count;
        if (LayoutTemplateMenu.Items.Count != expected)
            return false;
        if (LayoutTemplateMenu.Items[0] is not RadioMenuFlyoutItem builtIn || !ReferenceEquals(builtIn.Tag, _builtInLayout))
            return false;
        if (LayoutTemplateMenu.Items[1] is not RadioMenuFlyoutItem recording || !ReferenceEquals(recording.Tag, _recordingLayout))
            return false;
        var index = 2;
        if (_layoutTemplates.Count > 0)
        {
            if (LayoutTemplateMenu.Items[index] is not MenuFlyoutSeparator)
                return false;
            index++;
        }

        for (var i = 0; i < _layoutTemplates.Count; i++)
        {
            if (LayoutTemplateMenu.Items[index + i] is not RadioMenuFlyoutItem radio
                || !ReferenceEquals(radio.Tag, _layoutTemplates[i]))
                return false;
        }

        if (DeleteLayoutTemplateMenu.Items.Count != _layoutTemplates.Count)
            return false;
        for (var i = 0; i < _layoutTemplates.Count; i++)
        {
            if (DeleteLayoutTemplateMenu.Items[i] is not MenuFlyoutItem item
                || !ReferenceEquals(item.Tag, _layoutTemplates[i]))
                return false;
        }

        return true;
    }

    void RebuildLayoutTemplateMenu()
    {
        LayoutTemplateMenu.Items.Clear();
        LayoutTemplateMenu.Items.Add(LayoutTemplateItem(_builtInLayout));
        LayoutTemplateMenu.Items.Add(LayoutTemplateItem(_recordingLayout));
        if (_layoutTemplates.Count > 0)
            LayoutTemplateMenu.Items.Add(new MenuFlyoutSeparator());
        foreach (var template in _layoutTemplates)
            LayoutTemplateMenu.Items.Add(LayoutTemplateItem(template));

        DeleteLayoutTemplateMenu.Items.Clear();
        foreach (var template in _layoutTemplates)
        {
            var item = new MenuFlyoutItem
            {
                Text = template.Name,
                Tag = template,
            };
            item.Click += (_, _) => _ = DeleteLayoutTemplateAsync(template);
            DeleteLayoutTemplateMenu.Items.Add(item);
        }
    }

    RadioMenuFlyoutItem LayoutTemplateItem(LayoutTemplate template)
    {
        var item = new RadioMenuFlyoutItem
        {
            Text = template.Name,
            GroupName = "LayoutTemplate",
            Tag = template,
            IsChecked = ReferenceEquals(template, _appliedLayoutTemplate),
        };
        item.Click += (_, _) => ApplyLayoutTemplate(template);
        return item;
    }

    void SaveLayoutTemplate_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _ = SaveLayoutTemplateAsync();
    }

    async Task SaveLayoutTemplateAsync()
    {
        var name = await PresetUi.AskNameAsync(
            XamlRoot,
            "配置テンプレートを保存",
            "いまのパネルの表示と位置につける名前を入力してください。",
            _appliedLayoutTemplate is { BuiltIn: false } current ? current.Name : null,
            LayoutTemplateNameError,
            "テンプレート名");
        if (name == null)
            return;

        var existing = _layoutTemplates.FirstOrDefault(template => template.Name == name);
        if (existing != null && !ReferenceEquals(existing, _appliedLayoutTemplate)
            && !await PresetUi.ConfirmOverwriteAsync(
                XamlRoot,
                "配置テンプレートを上書きしますか？",
                $"「{name}」は既にあります。いまの配置で置き換えます。"))
            return;

        var target = existing ?? new LayoutTemplate { Name = name };
        target.Root = EditorLayout.Clone(Dock.Root);
        target.KeepRatios = Dock.KeepRatios;
        if (existing == null)
            _layoutTemplates.Add(target);
        _appliedLayoutTemplate = target;
        SyncLayoutTemplateMenu();
        try
        {
            LayoutTemplateStore.Save(_layoutTemplates);
        }
        catch (Exception ex)
        {
            ShowNotice("配置テンプレートを保存できませんでした", ex.Message);
        }
    }

    async Task DeleteLayoutTemplateAsync(LayoutTemplate template)
    {
        if (XamlRoot == null || !_layoutTemplates.Contains(template))
            return;
        var dialog = new ContentDialog
        {
            Title = "配置テンプレートを削除しますか？",
            Content = $"「{template.Name}」を削除します。",
            PrimaryButtonText = "削除する",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;
        if (!_layoutTemplates.Remove(template))
            return;
        if (ReferenceEquals(_appliedLayoutTemplate, template))
            _appliedLayoutTemplate = null;
        SyncLayoutTemplateMenu();
        try
        {
            LayoutTemplateStore.Save(_layoutTemplates);
        }
        catch (Exception ex)
        {
            ShowNotice("配置テンプレートを削除できませんでした", ex.Message);
        }
    }

    static string? LayoutTemplateNameError(string raw) =>
        PresetUi.ReservedNameError(
            raw,
            name => name is LayoutTemplate.BuiltInName or LayoutTemplate.RecordingName,
            "初期配置と録音という名前は使えません。");
}
