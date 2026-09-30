using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Otoasobi;

public sealed partial class EffectRack : UserControl
{
    readonly EffectChain _chain = new();
    readonly EqualizerEffect _eq = new();
    readonly List<EqPreset> _userPresets = [];
    StoredChain _stored = new();
    EqPreset? _selected;
    bool _ready;
    bool _dirty;
    bool _chainErrorShown;

    public EffectRack()
    {
        InitializeComponent();
        _chain.Add(_eq);
        Curve.Edited += (_, _) => PullFromCurve();
        Curve.EditCompleted += OnCurveCompleted;
        Curve.InteractionStarted += (_, _) => HostScroll.VerticalScrollMode = ScrollMode.Disabled;
        LoadPresets();
        LoadChain();
        EqToggle.Toggled += EqToggle_Toggled;
        _ready = true;
        UpdateChrome();
    }

    public EffectChain Chain => _chain;

    public void Process(Span<float> interleaved, int channels, int sampleRate) =>
        _chain.Process(interleaved, channels, sampleRate);

    public void Reset() => _chain.Reset();

    void LoadPresets()
    {
        _userPresets.Clear();
        _userPresets.AddRange(EqPresetStore.Load());
        RebuildPresetMenu();
        var flat = EqPreset.BuiltIns[0];
        _selected = flat;
        _eq.SetGains(flat.Gains);
        Curve.SetGains(flat.Gains);
        UpdateDirty();
    }

    void LoadChain()
    {
        var stored = EffectChainStore.Load();
        if (stored == null)
            return;
        _stored = stored;
        var entry = _stored.Effects.FirstOrDefault(effect => effect.Kind == EqualizerEffect.KindId);
        if (entry == null)
            return;
        _eq.Enabled = entry.Enabled;
        _eq.SetGains(entry.Gains);
        EqToggle.IsOn = entry.Enabled;
        Curve.SetGains(_eq.Gains);
        var named = FindPreset(entry.Preset);
        _selected = named ?? FindMatching(_eq.Gains);
        UpdateDirty();
    }

    void EqToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _eq.Enabled = EqToggle.IsOn;
        UpdateChrome();
        SaveChain();
    }

    void OnCurveCompleted(object? sender, EventArgs e)
    {
        HostScroll.VerticalScrollMode = ScrollMode.Enabled;
        PullFromCurve();
        SaveChain();
    }

    void PullFromCurve()
    {
        _eq.SetGains(Curve.Gains);
        UpdateReadout();
        UpdateDirty();
    }

    void UpdateChrome()
    {
        Curve.Opacity = EqToggle.IsOn ? 1 : 0.5;
        UpdateReadout();
    }

    void UpdateReadout()
    {
        var off = !EqToggle.IsOn;
        if (Curve.ActiveBand < 0)
        {
            EqReadout.Text = off
                ? "オフです。点をドラッグしてカーブを用意できます"
                : "点を上下にドラッグします";
            return;
        }

        var index = Curve.ActiveBand;
        var text = EqualizerEffect.FormatFrequency(EqualizerEffect.Frequencies[index])
            + "  ·  "
            + FormatDb(_eq.Gains[index]);
        EqReadout.Text = off ? "オフ  ·  " + text : text;
    }

    void RebuildPresetMenu()
    {
        PresetFlyout.Items.Clear();
        foreach (var preset in EqPreset.BuiltIns)
            AddPresetItem(preset);
        if (_userPresets.Count > 0)
            PresetFlyout.Items.Add(new MenuFlyoutSeparator());
        foreach (var preset in _userPresets)
            AddPresetItem(preset);
    }

    void AddPresetItem(EqPreset preset)
    {
        var item = new RadioMenuFlyoutItem
        {
            Text = preset.Name,
            GroupName = "eq-preset",
            Tag = preset,
            IsChecked = ReferenceEquals(preset, _selected),
        };
        item.Click += (_, _) => SelectPreset(preset);
        PresetFlyout.Items.Add(item);
    }

    void SelectPreset(EqPreset preset)
    {
        _selected = preset;
        _eq.SetGains(preset.Gains);
        Curve.SetGains(preset.Gains);
        UpdateChrome();
        UpdateDirty();
        SaveChain();
    }

    void UpdateDirty()
    {
        _dirty = _selected != null && !_eq.Matches(_selected.Gains);
        PresetNameText.Text = _selected?.Name ?? EqPreset.CustomName;
        PresetDirtyMark.Visibility = _dirty ? Visibility.Visible : Visibility.Collapsed;
        foreach (var entry in PresetFlyout.Items)
        {
            if (entry is not RadioMenuFlyoutItem radio || radio.Tag is not EqPreset preset)
                continue;
            radio.IsChecked = ReferenceEquals(preset, _selected);
            var text = _dirty && ReferenceEquals(preset, _selected) ? preset.Name + " *" : preset.Name;
            if (radio.Text != text)
                radio.Text = text;
        }
    }

    EqPreset? FindPreset(string name)
    {
        if (name.Length == 0)
            return null;
        return EqPreset.BuiltIns.FirstOrDefault(preset => preset.Name == name)
            ?? _userPresets.FirstOrDefault(preset => preset.Name == name);
    }

    EqPreset? FindMatching(IReadOnlyList<double> gains)
    {
        return EqPreset.BuiltIns.FirstOrDefault(preset => Same(preset.Gains, gains))
            ?? _userPresets.FirstOrDefault(preset => Same(preset.Gains, gains));
    }

    static bool Same(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        if (left.Count < EqualizerEffect.BandCount || right.Count < EqualizerEffect.BandCount)
            return false;
        for (var i = 0; i < EqualizerEffect.BandCount; i++)
        {
            if (Math.Round(left[i], 1) != Math.Round(right[i], 1))
                return false;
        }

        return true;
    }

    void SaveChain()
    {
        if (!_ready)
            return;
        var entry = _stored.Effects.FirstOrDefault(effect => effect.Kind == EqualizerEffect.KindId);
        if (entry == null)
        {
            entry = new StoredEffect { Kind = EqualizerEffect.KindId };
            _stored.Effects.Add(entry);
        }

        entry.Enabled = _eq.Enabled;
        entry.Preset = _selected?.Name ?? "";
        entry.Gains = _eq.CopyGains();
        try
        {
            EffectChainStore.Save(_stored);
            _chainErrorShown = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_chainErrorShown)
                return;
            _chainErrorShown = true;
            _ = TellAsync("状態を保存できませんでした", ex.Message);
        }
    }

    void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        _ = SavePresetAsync();
    }

    async Task SavePresetAsync()
    {
        var box = new TextBox { PlaceholderText = "プリセット名" };
        if (_selected is { BuiltIn: false })
        {
            box.Text = _selected.Name;
            box.SelectAll();
        }

        var error = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        if (Application.Current.Resources.TryGetValue("SystemFillColorCriticalBrush", out var brush) && brush is Brush paint)
            error.Foreground = paint;
        var panel = new StackPanel { Spacing = 8, MinWidth = 280 };
        panel.Children.Add(new TextBlock
        {
            Text = "このカーブにつける名前を入力してください。",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(box);
        panel.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = "現在のカーブを保存",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        dialog.Opened += (_, _) => box.Focus(FocusState.Programmatic);
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var message = NameError(box.Text);
            if (message == null)
                return;
            args.Cancel = true;
            error.Text = message;
            error.Visibility = Visibility.Visible;
        };
        if (XamlRoot == null || await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var name = box.Text.Trim();
        var existing = _userPresets.FirstOrDefault(preset => preset.Name == name);
        if (existing != null && !ReferenceEquals(existing, _selected))
        {
            var confirm = new ContentDialog
            {
                Title = "プリセットを上書きしますか？",
                Content = $"「{name}」は既にあります。いまのカーブで置き換えます。",
                PrimaryButtonText = "上書きする",
                CloseButtonText = "キャンセル",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                return;
        }

        var target = existing ?? new EqPreset { Name = name };
        target.Gains = _eq.CopyGains();
        if (existing == null)
            _userPresets.Add(target);
        try
        {
            EqPresetStore.Save(_userPresets);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await TellAsync("プリセットを保存できませんでした", ex.Message);
            return;
        }

        RebuildPresetMenu();
        SelectPreset(target);
    }

    static string? NameError(string raw)
    {
        var name = raw.Trim();
        if (name.Length == 0)
            return "名前を入力してください。";
        if (name.Length > 40)
            return "名前は40文字までです。";
        if (name.Any(char.IsControl))
            return "名前に使えない文字があります。";
        if (EqPreset.IsReservedName(name))
            return "その名前は使えません。";
        return null;
    }

    async Task TellAsync(string title, string message)
    {
        if (XamlRoot == null)
            return;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "閉じる",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    static string FormatDb(double gain)
    {
        var rounded = Math.Round(gain, 1);
        var text = Math.Abs(rounded).ToString("0.0", CultureInfo.InvariantCulture);
        if (rounded > 0)
            return "+" + text + " dB";
        if (rounded < 0)
            return "-" + text + " dB";
        return "0.0 dB";
    }
}
