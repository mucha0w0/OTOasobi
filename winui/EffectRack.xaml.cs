using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Otoasobi;

public sealed partial class EffectRack : UserControl
{
    readonly EffectChain _chain = new();
    readonly EqualizerEffect _eq = new();
    readonly NormalizeEffect _norm = new() { Enabled = false };
    readonly List<EqPreset> _userPresets = [];
    StoredChain _stored = new();
    EqPreset? _selected;
    Border? _dragCard;
    int _dropIndex = -1;
    Border? _marked;
    Brush? _markedBrush;
    Thickness _markedThickness;
    bool _ready;
    bool _dirty;
    bool _chainErrorShown;

    public EffectRack()
    {
        InitializeComponent();
        EqGrip.Tag = EqCard;
        NormGrip.Tag = NormCard;
        _chain.SetOrder([_eq, _norm]);
        Curve.Edited += (_, _) => PullFromCurve();
        Curve.EditCompleted += OnCurveCompleted;
        Curve.InteractionStarted += (_, _) => HostScroll.VerticalScrollMode = ScrollMode.Disabled;
        Curve.PointerEntered += (_, _) => HostScroll.VerticalScrollMode = ScrollMode.Disabled;
        Curve.PointerExited += (_, _) =>
        {
            if (!Curve.IsDragging && _dragCard == null)
                HostScroll.VerticalScrollMode = ScrollMode.Enabled;
        };
        LoadPresets();
        LoadChain();
        EqToggle.Toggled += EqToggle_Toggled;
        NormToggle.Toggled += NormToggle_Toggled;
        NormSlider.ValueChanged += (_, _) =>
        {
            _norm.TargetDb = NormSlider.Value;
            UpdateNormChrome();
        };
        NormSlider.PointerReleased += (_, _) => SaveChain();
        NormSlider.LostFocus += (_, _) => SaveChain();
        _ready = true;
        UpdateChrome();
        UpdateNormChrome();
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
                Curve.SetGains(_eq.Gains);
                _selected = FindPreset(entry.Preset) ?? FindMatching(_eq.Gains);
                order.Add(_eq);
            }
            else if (entry.Kind == NormalizeEffect.KindId && seen.Add(entry.Kind))
            {
                _norm.Enabled = entry.Enabled;
                _norm.TargetDb = entry.Target;
                NormToggle.IsOn = entry.Enabled;
                NormSlider.Value = _norm.TargetDb;
                order.Add(_norm);
            }
        }

        if (seen.Add(EqualizerEffect.KindId))
            order.Add(_eq);
        if (seen.Add(NormalizeEffect.KindId))
            order.Add(_norm);
        _chain.SetOrder(order);
        LayoutCards();
        UpdateDirty();
    }

    void LayoutCards()
    {
        EffectList.Children.Clear();
        foreach (var effect in _chain.Effects)
        {
            if (ReferenceEquals(effect, _eq))
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

    void UpdateNormChrome()
    {
        NormBody.Opacity = NormToggle.IsOn ? 1 : 0.5;
        NormValue.Text = FormatDb(_norm.TargetDb);
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
        if (!Curve.IsDragging)
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
                    Target = _norm.TargetDb,
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
