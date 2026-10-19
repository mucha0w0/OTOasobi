using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Otoasobi;

public sealed partial class MainPage
{
    const double DefaultVcConsonantPercent = 100;
    const double DefaultVcBlankPercent = 100;
    const string CvvcOverlapTip = "0% はオフセット（子音の開始）、100% は先行発声（母音の開始）です。";
    const string VcConsonantTip = "0% は先行発声（子音の開始）、100% はブランクの位置です。";
    const string VcBlankTip = "0% はその子音の開始、100% は終了です。";

    readonly Dictionary<string, Slider> _cvvcOverlapSliders = [];
    readonly Dictionary<string, TextBlock> _cvvcOverlapValues = [];
    readonly Dictionary<string, Slider> _vcConsonantSliders = [];
    readonly Dictionary<string, TextBlock> _vcConsonantValues = [];
    readonly Dictionary<string, Slider> _vcBlankSliders = [];
    readonly Dictionary<string, TextBlock> _vcBlankValues = [];
    readonly CvvcPreset _cvvcBuiltIn = new()
    {
        Name = CvvcPreset.BuiltInName,
        BuiltIn = true,
        Consonant = DefaultConsonantSeconds,
        Blank = DefaultBlankSeconds,
        VcOverlap = DefaultOverlapPercent,
    };
    readonly List<CvvcPreset> _cvvcPresets = [];
    CvvcPreset? _selectedCvvc;
    OtoPreset _leadPreset = null!;

    void BuildCvvcSliders()
    {
        BuildPercentSliders(CvvcOverlapList, _cvvcOverlapSliders, _cvvcOverlapValues, DefaultOverlapPercent, "通常音素のオーバーラップ", CvvcOverlapTip);
        BuildPercentSliders(VcConsonantList, _vcConsonantSliders, _vcConsonantValues, DefaultVcConsonantPercent, "子音音素の子音部", VcConsonantTip);
        BuildPercentSliders(VcBlankList, _vcBlankSliders, _vcBlankValues, DefaultVcBlankPercent, "子音音素のブランク", VcBlankTip);
    }

    void BuildPercentSliders(
        StackPanel list,
        Dictionary<string, Slider> sliders,
        Dictionary<string, TextBlock> values,
        double initial,
        string automationPrefix,
        string tip)
    {
        var valueStyle = (Style)Resources["OverlapValueStyle"];
        var groupStyle = (Style)Resources["OverlapGroupStyle"];
        string? group = null;
        StackPanel? rows = null;
        foreach (var (name, key, caption) in ConsonantOverlap.Kinds)
        {
            if (name != group)
            {
                group = name;
                rows = new StackPanel { Spacing = 8 };
                var section = new StackPanel { Spacing = 8 };
                section.Children.Add(new TextBlock { Text = name, Style = groupStyle });
                section.Children.Add(rows);
                list.Children.Add(section);
            }

            var value = new TextBlock { Text = FormatPercent(initial), Style = valueStyle };
            var head = new Grid();
            head.Children.Add(new TextBlock { Text = caption, VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(value);
            var slider = new Slider
            {
                Maximum = 100,
                Minimum = 0,
                StepFrequency = 1,
                SmallChange = 1,
                SnapsTo = SliderSnapsTo.StepValues,
                Tag = key,
                Value = initial,
            };
            AutomationProperties.SetName(slider, automationPrefix + " " + caption);
            ToolTipService.SetToolTip(slider, tip);
            sliders[key] = slider;
            values[key] = value;
            slider.ValueChanged += (_, _) => OnCvvcSliderChanged();
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(head);
            row.Children.Add(slider);
            rows!.Children.Add(row);
        }
    }

    void OnCvvcSliderChanged()
    {
        if (_applyingPreset)
            return;
        OnConfigChanged();
    }

    void UpdateCvvcTexts()
    {
        CvvcConsonantValue.Text = FormatSeconds(CvvcConsonantSlider.Value);
        CvvcBlankValue.Text = FormatSignedSeconds(CvvcBlankSlider.Value);
        VcOffsetValue.Text = FormatSignedSeconds(VcOffsetSlider.Value);
        VcOverlapValue.Text = FormatPercent(VcOverlapSlider.Value);
        WritePercents(_cvvcOverlapSliders, _cvvcOverlapValues);
        WritePercents(_vcConsonantSliders, _vcConsonantValues);
        WritePercents(_vcBlankSliders, _vcBlankValues);
    }

    static void WritePercents(Dictionary<string, Slider> sliders, Dictionary<string, TextBlock> values)
    {
        foreach (var (key, value) in values)
        {
            if (sliders.TryGetValue(key, out var slider))
                value.Text = FormatPercent(slider.Value);
        }
    }

    OtoExport CvvcExport()
    {
        return new(
            0,
            Math.Round(CvvcConsonantSlider.Value * 1000.0),
            CvvcBlankSlider.Value,
            PercentMap(_cvvcOverlapSliders, DefaultOverlapPercent),
            "cvvc",
            0.5,
            0,
            200,
            0,
            null,
            Math.Round(-VcOffsetSlider.Value * 1000.0),
            PercentMap(_vcConsonantSliders, DefaultVcConsonantPercent),
            PercentMap(_vcBlankSliders, DefaultVcBlankPercent),
            VcOverlapSlider.Value / 100.0,
            Math.Round(-_leadPreset.Offset * 1000.0),
            Math.Round(_leadPreset.Consonant * 1000.0),
            _leadPreset.Blank,
            RestOverlap(_leadPreset));
    }

    static Dictionary<string, double> PercentMap(Dictionary<string, Slider> sliders, double fallback)
    {
        var map = new Dictionary<string, double>(ConsonantOverlap.Kinds.Length);
        foreach (var (_, key, _) in ConsonantOverlap.Kinds)
        {
            var percent = sliders.TryGetValue(key, out var slider) ? slider.Value : fallback;
            map[key] = Math.Clamp(percent, 0, 100) / 100.0;
        }

        return map;
    }

    void LoadCvvcPresets()
    {
        _cvvcPresets.Clear();
        _cvvcPresets.AddRange(CvvcPresetStore.Load());
        RebuildCvvcPresetMenu();
        SelectCvvcPreset(_cvvcBuiltIn);
    }

    void RebuildCvvcLeadMenu()
    {
        if (_leadPreset != _builtIn && !_userPresets.Contains(_leadPreset))
            _leadPreset = _builtIn;
        PresetUi.Fill(
            CvvcLeadPresetFlyout,
            "cvvc-lead-preset",
            [_builtIn],
            _userPresets,
            _leadPreset,
            SelectCvvcLead,
            separator: false);
        CvvcLeadPresetNameText.Text = _leadPreset.Name;
    }

    void SelectCvvcLead(OtoPreset preset)
    {
        _leadPreset = preset;
        CvvcLeadPresetNameText.Text = preset.Name;
        foreach (var entry in CvvcLeadPresetFlyout.Items)
        {
            if (entry is RadioMenuFlyoutItem radio && ReferenceEquals(radio.Tag, preset))
                radio.IsChecked = true;
        }

        if (_configReady)
            OnConfigChanged();
    }

    void RebuildCvvcPresetMenu() =>
        PresetUi.Fill(
            CvvcPresetFlyout,
            "cvvc-preset",
            [_cvvcBuiltIn],
            _cvvcPresets,
            _selectedCvvc,
            SelectCvvcPreset,
            separator: false);

    void SelectCvvcPreset(CvvcPreset preset)
    {
        _selectedCvvc = preset;
        CvvcPresetNameText.Text = preset.Name;
        foreach (var entry in CvvcPresetFlyout.Items)
        {
            if (entry is RadioMenuFlyoutItem radio && ReferenceEquals(radio.Tag, preset))
                radio.IsChecked = true;
        }

        ApplyCvvcPreset(preset);
    }

    void ApplyCvvcPreset(CvvcPreset preset)
    {
        _applyingPreset = true;
        CvvcConsonantSlider.Value = preset.Consonant;
        CvvcBlankSlider.Value = preset.Blank;
        VcOffsetSlider.Value = preset.VcOffset;
        VcOverlapSlider.Value = preset.VcOverlap;
        ApplyPercents(_cvvcOverlapSliders, preset.Overlap, DefaultOverlapPercent);
        ApplyPercents(_vcConsonantSliders, preset.VcConsonant, DefaultVcConsonantPercent);
        ApplyPercents(_vcBlankSliders, preset.VcBlank, DefaultVcBlankPercent);
        _applyingPreset = false;
        OnConfigChanged();
    }

    static void ApplyPercents(Dictionary<string, Slider> sliders, Dictionary<string, double> table, double fallback)
    {
        foreach (var (key, slider) in sliders)
            slider.Value = StoredPercent(table, key, fallback);
    }

    static double StoredPercent(Dictionary<string, double> table, string key, double fallback)
    {
        return table.TryGetValue(key, out var value) ? CvvcPresetStore.Percent(value, fallback) : fallback;
    }

    void UpdateCvvcPresetDirty()
    {
        var dirty = !_applyingPreset && _selectedCvvc != null && !CvvcPresetMatches(_selectedCvvc);
        PresetUi.MarkDirty(CvvcPresetFlyout, null, CvvcPresetDirtyMark, _selectedCvvc, dirty, CvvcPreset.BuiltInName);
    }

    bool CvvcPresetMatches(CvvcPreset preset)
    {
        if (Math.Round(CvvcConsonantSlider.Value, 3) != Math.Round(preset.Consonant, 3))
            return false;
        if (Math.Round(CvvcBlankSlider.Value, 3) != Math.Round(preset.Blank, 3))
            return false;
        if (Math.Round(VcOffsetSlider.Value, 3) != Math.Round(preset.VcOffset, 3))
            return false;
        if (Math.Round(VcOverlapSlider.Value) != Math.Round(preset.VcOverlap))
            return false;
        return PercentsMatch(_cvvcOverlapSliders, preset.Overlap, DefaultOverlapPercent)
            && PercentsMatch(_vcConsonantSliders, preset.VcConsonant, DefaultVcConsonantPercent)
            && PercentsMatch(_vcBlankSliders, preset.VcBlank, DefaultVcBlankPercent);
    }

    static bool PercentsMatch(Dictionary<string, Slider> sliders, Dictionary<string, double> table, double fallback)
    {
        foreach (var (key, slider) in sliders)
        {
            if (Math.Round(slider.Value) != Math.Round(StoredPercent(table, key, fallback)))
                return false;
        }

        return true;
    }

    void CopyCvvcInto(CvvcPreset preset)
    {
        preset.Consonant = Math.Round(CvvcConsonantSlider.Value, 3);
        preset.Blank = Math.Round(CvvcBlankSlider.Value, 3);
        preset.VcOffset = Math.Round(VcOffsetSlider.Value, 3);
        preset.VcOverlap = CvvcPresetStore.Percent(VcOverlapSlider.Value, DefaultOverlapPercent);
        CopyPercents(preset.Overlap, _cvvcOverlapSliders, DefaultOverlapPercent);
        CopyPercents(preset.VcConsonant, _vcConsonantSliders, DefaultVcConsonantPercent);
        CopyPercents(preset.VcBlank, _vcBlankSliders, DefaultVcBlankPercent);
    }

    static void CopyPercents(Dictionary<string, double> dest, Dictionary<string, Slider> sliders, double fallback)
    {
        dest.Clear();
        foreach (var (key, slider) in sliders)
            dest[key] = CvvcPresetStore.Percent(slider.Value, fallback);
    }

    void SaveCvvcPreset_Click(object sender, RoutedEventArgs e)
    {
        _ = SaveCvvcPresetAsync();
    }

    async Task SaveCvvcPresetAsync()
    {
        var name = await PresetUi.AskNameAsync(
            XamlRoot,
            "現在の設定を保存",
            "この設定につける名前を入力してください。",
            _selectedCvvc is { BuiltIn: false } ? _selectedCvvc.Name : null,
            PresetNameError);
        if (name == null)
            return;

        var existing = _cvvcPresets.FirstOrDefault(preset => preset.Name == name);
        if (existing != null && !ReferenceEquals(existing, _selectedCvvc)
            && !await PresetUi.ConfirmOverwriteAsync(
                XamlRoot,
                "プリセットを上書きしますか？",
                $"「{name}」は既にあります。いまの設定で置き換えます。"))
            return;

        var target = existing ?? new CvvcPreset { Name = name };
        CopyCvvcInto(target);
        if (existing == null)
            _cvvcPresets.Add(target);
        RebuildCvvcPresetMenu();
        SelectCvvcPreset(target);
        try
        {
            CvvcPresetStore.Save(_cvvcPresets);
        }
        catch (Exception ex)
        {
            ShowNotice("プリセットを保存できませんでした", ex.Message);
        }
    }
}
