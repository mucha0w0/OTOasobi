using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Otoasobi;

public sealed partial class EffectRack : UserControl
{
    readonly EffectChain _chain = new();
    readonly HighPassEffect _hp = new();
    readonly EqualizerEffect _eq = new();
    readonly NormalizeEffect _norm = new() { Enabled = false };
    readonly List<EqPreset> _userPresets = [];
    readonly List<NormalizePreset> _normPresets = [];
    readonly Slider[] _bandSliders = new Slider[EqualizerEffect.BandCount];
    readonly TextBlock[] _bandValues = new TextBlock[EqualizerEffect.BandCount];
    StoredChain _stored = new();
    EqPreset? _selected;
    NormalizePreset? _normSelected;
    Border? _dragCard;
    int _dropIndex = -1;
    Border? _marked;
    Brush? _markedBrush;
    Thickness _markedThickness;
    bool _ready;
    bool _dirty;
    bool _applying;
    bool _chainErrorShown;

    public EffectRack()
    {
        InitializeComponent();
        EqGrip.Tag = EqCard;
        NormGrip.Tag = NormCard;
        HpGrip.Tag = HpCard;
        _chain.SetOrder([_hp, _eq, _norm]);
        BuildEqSliders();
        LoadPresets();
        LoadNormPresets();
        LoadChain();
        EqToggle.Toggled += EqToggle_Toggled;
        NormToggle.Toggled += NormToggle_Toggled;
        HpToggle.Toggled += HpToggle_Toggled;
        HpFrequency.ValueChanged += (_, _) =>
        {
            if (_applying)
                return;
            _hp.Frequency = HpFrequency.Value;
            UpdateHpChrome();
        };
        HpFrequency.PointerReleased += (_, _) => SaveChain();
        HpFrequency.LostFocus += (_, _) => SaveChain();
        HpSlope12.Checked += HpSlope_Checked;
        HpSlope24.Checked += HpSlope_Checked;
        NormSlider.ValueChanged += (_, _) =>
        {
            if (_applying)
                return;
            _norm.TargetDb = NormSlider.Value;
            UpdateNormChrome();
        };
        NormSlider.PointerReleased += (_, _) => SaveChain();
        NormSlider.LostFocus += (_, _) => SaveChain();
        _ready = true;
        UpdateChrome();
        UpdateNormChrome();
        UpdateHpChrome();
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
        ShowGains();
        UpdateDirty();
    }

    void LoadChain()
    {
        var stored = EffectChainStore.Load();
        if (stored != null)
            _stored = stored;
        var order = new List<AudioEffect>();
        var seen = new HashSet<string>();
        foreach (var entry in _stored.Effects)
        {
            if (entry.Kind == EqualizerEffect.KindId && seen.Add(entry.Kind))
            {
                _eq.Enabled = entry.Enabled;
                _eq.SetGains(entry.Gains);
                EqToggle.IsOn = entry.Enabled;
                ShowGains();
                _selected = FindPreset(entry.Preset) ?? FindMatching(_eq.Gains);
                order.Add(_eq);
            }
            else if (entry.Kind == NormalizeEffect.KindId && seen.Add(entry.Kind))
            {
                _norm.Enabled = entry.Enabled;
                _norm.TargetDb = entry.Target;
                NormToggle.IsOn = entry.Enabled;
                NormSlider.Value = _norm.TargetDb;
                _normSelected = FindNormPreset(entry.Preset) ?? FindMatchingNorm(_norm.TargetDb);
                order.Add(_norm);
            }
            else if (entry.Kind == HighPassEffect.KindId && seen.Add(entry.Kind))
            {
                _hp.Enabled = entry.Enabled;
                _hp.Frequency = entry.Frequency;
                _hp.Slope = entry.Slope;
                ShowHighPass();
                order.Add(_hp);
            }
        }

        if (seen.Add(HighPassEffect.KindId))
            order.Insert(0, _hp);
        if (seen.Add(EqualizerEffect.KindId))
            order.Add(_eq);
        if (seen.Add(NormalizeEffect.KindId))
            order.Add(_norm);
        _chain.SetOrder(order);
        LayoutCards();
        UpdateDirty();
        UpdateNormDirty();
        UpdateHpChrome();
    }

    void LoadNormPresets()
    {
        _normPresets.Clear();
        _normPresets.AddRange(NormalizePresetStore.Load());
        RebuildNormPresetMenu();
        var standard = NormalizePreset.BuiltIns.First(preset => preset.Matches(NormalizeEffect.DefaultDb));
        _normSelected = standard;
        _norm.TargetDb = standard.Target;
        NormSlider.Value = _norm.TargetDb;
        UpdateNormDirty();
    }

    void LayoutCards()
    {
        EffectList.Children.Clear();
        foreach (var effect in _chain.Effects)
        {
            if (ReferenceEquals(effect, _hp))
                EffectList.Children.Add(HpCard);
            else if (ReferenceEquals(effect, _eq))
                EffectList.Children.Add(EqCard);
            else if (ReferenceEquals(effect, _norm))
                EffectList.Children.Add(NormCard);
        }
    }

    void EqToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _eq.Enabled = EqToggle.IsOn;
        UpdateChrome();
        SaveChain();
    }

    void NormToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _norm.Enabled = NormToggle.IsOn;
        UpdateNormChrome();
        SaveChain();
    }

    void HpToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _hp.Enabled = HpToggle.IsOn;
        UpdateHpChrome();
        SaveChain();
    }

    void HpSlope_Checked(object sender, RoutedEventArgs e)
    {
        if (_applying)
            return;
        var slope = HpSlope24.IsChecked == true ? HighPassEffect.Slope24 : HighPassEffect.Slope12;
        if (slope == _hp.Slope)
            return;
        _hp.Slope = slope;
        SaveChain();
    }

    void ShowHighPass()
    {
        _applying = true;
        HpToggle.IsOn = _hp.Enabled;
        HpFrequency.Value = _hp.Frequency;
        if (_hp.Slope == HighPassEffect.Slope24)
            HpSlope24.IsChecked = true;
        else
            HpSlope12.IsChecked = true;
        _applying = false;
    }

    void UpdateHpChrome()
    {
        HpBody.Opacity = HpToggle.IsOn ? 1 : 0.5;
        HpFrequencyValue.Text = FormatHz(_hp.Frequency);
    }

    void UpdateNormChrome()
    {
        NormBody.Opacity = NormToggle.IsOn ? 1 : 0.5;
        NormValue.Text = FormatDb(_norm.TargetDb);
        UpdateNormDirty();
    }

    void Grip_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border grip || grip.Tag is not Border card)
            return;
        var point = e.GetCurrentPoint(grip);
        if (!point.Properties.IsLeftButtonPressed)
            return;
        _dragCard = card;
        _dropIndex = EffectList.Children.IndexOf(card);
        card.Opacity = 0.55;
        HostScroll.VerticalScrollMode = ScrollMode.Disabled;
        grip.CancelDirectManipulations();
        grip.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    void Grip_Moved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragCard == null)
            return;
        _dropIndex = IndexAt(e.GetCurrentPoint(EffectList).Position);
        MarkDrop(_dropIndex);
        e.Handled = true;
    }

    void Grip_Released(object sender, PointerRoutedEventArgs e)
    {
        var from = _dragCard == null ? -1 : EffectList.Children.IndexOf(_dragCard);
        var to = _dropIndex;
        EndGrip();
        if (sender is UIElement grip)
        {
            foreach (var captured in grip.PointerCaptures)
            {
                if (captured.PointerId == e.Pointer.PointerId)
                {
                    grip.ReleasePointerCapture(e.Pointer);
                    break;
                }
            }
        }

        CommitGrip(from, to);
        e.Handled = true;
    }

    void Grip_CaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_dragCard == null)
            return;
        var from = EffectList.Children.IndexOf(_dragCard);
        var to = _dropIndex;
        EndGrip();
        CommitGrip(from, to);
    }

    void EndGrip()
    {
        if (_dragCard != null)
            _dragCard.Opacity = 1;
        _dragCard = null;
        RestoreMark();
        HostScroll.VerticalScrollMode = ScrollMode.Enabled;
    }

    void CommitGrip(int from, int to)
    {
        if (from < 0 || to < 0 || from == to)
            return;
        _chain.Move(from, to);
        LayoutCards();
        SaveChain();
    }

    int IndexAt(Windows.Foundation.Point point)
    {
        var count = EffectList.Children.Count;
        for (var i = 0; i < count; i++)
        {
            if (EffectList.Children[i] is not FrameworkElement element)
                continue;
            var top = element.TransformToVisual(EffectList).TransformPoint(default).Y;
            if (point.Y < top + element.ActualHeight * 0.5)
                return i;
        }

        return Math.Max(0, count - 1);
    }

    void MarkDrop(int index)
    {
        if (index < 0 || index >= EffectList.Children.Count || EffectList.Children[index] is not Border border)
        {
            RestoreMark();
            return;
        }

        if (ReferenceEquals(_marked, border))
            return;
        RestoreMark();
        _marked = border;
        _markedBrush = border.BorderBrush;
        _markedThickness = border.BorderThickness;
        border.BorderBrush = Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush ?? border.BorderBrush;
    }

    void RestoreMark()
    {
        if (_marked == null)
            return;
        _marked.BorderBrush = _markedBrush;
        _marked.BorderThickness = _markedThickness;
        _marked = null;
        _markedBrush = null;
    }

    void BuildEqSliders()
    {
        var valueStyle = (Style)Resources["BandValueStyle"];
        for (var i = 0; i < EqualizerEffect.BandCount; i++)
        {
            var freq = EqualizerEffect.FormatFrequency(EqualizerEffect.Frequencies[i]);
            var value = new TextBlock { Text = FormatDb(0), Style = valueStyle };
            var head = new Grid();
            head.Children.Add(new TextBlock { Text = freq, VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(value);
            var slider = new Slider
            {
                Minimum = EqualizerEffect.MinDb,
                Maximum = EqualizerEffect.MaxDb,
                StepFrequency = 0.1,
                SnapsTo = SliderSnapsTo.StepValues,
                Value = 0,
            };
            AutomationProperties.SetName(slider, freq);
            var index = i;
            slider.ValueChanged += (_, _) => OnBandChanged(index);
            slider.PointerReleased += (_, _) => SaveChain();
            slider.LostFocus += (_, _) => SaveChain();
            var row = new StackPanel { Spacing = 2 };
            row.Children.Add(head);
            row.Children.Add(slider);
            EqBody.Children.Add(row);
            _bandSliders[i] = slider;
            _bandValues[i] = value;
        }
    }

    void OnBandChanged(int index)
    {
        if (_applying)
            return;
        var gains = new double[EqualizerEffect.BandCount];
        for (var i = 0; i < gains.Length; i++)
            gains[i] = _bandSliders[i].Value;
        _eq.SetGains(gains);
        _bandValues[index].Text = FormatDb(_eq.Gains[index]);
        UpdateDirty();
    }

    void ShowGains()
    {
        _applying = true;
        for (var i = 0; i < EqualizerEffect.BandCount; i++)
        {
            _bandSliders[i].Value = _eq.Gains[i];
            _bandValues[i].Text = FormatDb(_eq.Gains[i]);
        }

        _applying = false;
    }

    void UpdateChrome()
    {
        EqBody.Opacity = EqToggle.IsOn ? 1 : 0.5;
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
        ShowGains();
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
        var known = new HashSet<string>();
        var list = new List<StoredEffect>();
        foreach (var effect in _chain.Effects)
        {
            known.Add(effect.Kind);
            if (ReferenceEquals(effect, _eq))
            {
                list.Add(new StoredEffect
                {
                    Kind = EqualizerEffect.KindId,
                    Enabled = _eq.Enabled,
                    Preset = _selected?.Name ?? "",
                    Gains = _eq.CopyGains(),
                });
            }
            else if (ReferenceEquals(effect, _norm))
            {
                list.Add(new StoredEffect
                {
                    Kind = NormalizeEffect.KindId,
                    Enabled = _norm.Enabled,
                    Preset = _normSelected?.Name ?? "",
                    Target = _norm.TargetDb,
                });
            }
            else if (ReferenceEquals(effect, _hp))
            {
                list.Add(new StoredEffect
                {
                    Kind = HighPassEffect.KindId,
                    Enabled = _hp.Enabled,
                    Frequency = _hp.Frequency,
                    Slope = _hp.Slope,
                });
            }
        }

        foreach (var old in _stored.Effects)
        {
            if (old.Kind.Length > 0 && !known.Contains(old.Kind))
                list.Add(old);
        }

        _stored.Effects = list;
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
            Text = "この設定につける名前を入力してください。",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(box);
        panel.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = "現在の設定を保存",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        dialog.Opened += (_, _) => box.Focus(FocusState.Programmatic);
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var message = NameError(box.Text, EqPreset.IsReservedName);
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
                Content = $"「{name}」は既にあります。いまの設定で置き換えます。",
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

    void RebuildNormPresetMenu()
    {
        NormPresetFlyout.Items.Clear();
        foreach (var preset in NormalizePreset.BuiltIns)
            AddNormPresetItem(preset);
        if (_normPresets.Count > 0)
            NormPresetFlyout.Items.Add(new MenuFlyoutSeparator());
        foreach (var preset in _normPresets)
            AddNormPresetItem(preset);
    }

    void AddNormPresetItem(NormalizePreset preset)
    {
        var item = new RadioMenuFlyoutItem
        {
            Text = preset.Name,
            GroupName = "norm-preset",
            Tag = preset,
            IsChecked = ReferenceEquals(preset, _normSelected),
        };
        item.Click += (_, _) => SelectNormPreset(preset);
        NormPresetFlyout.Items.Add(item);
    }

    void SelectNormPreset(NormalizePreset preset)
    {
        _normSelected = preset;
        _norm.TargetDb = preset.Target;
        _applying = true;
        NormSlider.Value = _norm.TargetDb;
        _applying = false;
        UpdateNormChrome();
        SaveChain();
    }

    void UpdateNormDirty()
    {
        var dirty = _normSelected != null && !_normSelected.Matches(_norm.TargetDb);
        NormPresetNameText.Text = _normSelected?.Name ?? NormalizePreset.CustomName;
        NormPresetDirtyMark.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        foreach (var entry in NormPresetFlyout.Items)
        {
            if (entry is not RadioMenuFlyoutItem radio || radio.Tag is not NormalizePreset preset)
                continue;
            radio.IsChecked = ReferenceEquals(preset, _normSelected);
            var text = dirty && ReferenceEquals(preset, _normSelected) ? preset.Name + " *" : preset.Name;
            if (radio.Text != text)
                radio.Text = text;
        }
    }

    NormalizePreset? FindNormPreset(string name)
    {
        if (name.Length == 0)
            return null;
        return NormalizePreset.BuiltIns.FirstOrDefault(preset => preset.Name == name)
            ?? _normPresets.FirstOrDefault(preset => preset.Name == name);
    }

    NormalizePreset? FindMatchingNorm(double target) =>
        NormalizePreset.BuiltIns.FirstOrDefault(preset => preset.Matches(target))
        ?? _normPresets.FirstOrDefault(preset => preset.Matches(target));

    void SaveNormPreset_Click(object sender, RoutedEventArgs e)
    {
        _ = SaveNormPresetAsync();
    }

    async Task SaveNormPresetAsync()
    {
        var box = new TextBox { PlaceholderText = "プリセット名" };
        if (_normSelected is { BuiltIn: false })
        {
            box.Text = _normSelected.Name;
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
            Text = "このピークにつける名前を入力してください。",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(box);
        panel.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = "現在のピークを保存",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        dialog.Opened += (_, _) => box.Focus(FocusState.Programmatic);
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var message = NameError(box.Text, NormalizePreset.IsReservedName);
            if (message == null)
                return;
            args.Cancel = true;
            error.Text = message;
            error.Visibility = Visibility.Visible;
        };
        if (XamlRoot == null || await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var name = box.Text.Trim();
        var existing = _normPresets.FirstOrDefault(preset => preset.Name == name);
        if (existing != null && !ReferenceEquals(existing, _normSelected))
        {
            var confirm = new ContentDialog
            {
                Title = "プリセットを上書きしますか？",
                Content = $"「{name}」は既にあります。いまのピークで置き換えます。",
                PrimaryButtonText = "上書きする",
                CloseButtonText = "キャンセル",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                return;
        }

        var target = existing ?? new NormalizePreset { Name = name };
        target.Target = _norm.TargetDb;
        if (existing == null)
            _normPresets.Add(target);
        try
        {
            NormalizePresetStore.Save(_normPresets);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await TellAsync("プリセットを保存できませんでした", ex.Message);
            return;
        }

        RebuildNormPresetMenu();
        SelectNormPreset(target);
    }

    static string? NameError(string raw, Func<string, bool> reserved)
    {
        var name = raw.Trim();
        if (name.Length == 0)
            return "名前を入力してください。";
        if (name.Length > 40)
            return "名前は40文字までです。";
        if (name.Any(char.IsControl))
            return "名前に使えない文字があります。";
        if (reserved(name))
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

    static string FormatHz(double hz) =>
        Math.Round(hz).ToString("0", CultureInfo.InvariantCulture) + " Hz";
}
