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
    readonly NoiseSuppressorEffect _noise = new() { Enabled = false };
    readonly NoiseWaveform _wave = new();
    readonly HighPassEffect _hp = new();
    readonly EqualizerEffect _eq = new();
    readonly CompressorEffect _comp = new();
    readonly NormalizeEffect _norm = new() { Enabled = false };
    readonly List<EqPreset> _userPresets = [];
    readonly List<NormalizePreset> _normPresets = [];
    readonly List<HighPassPreset> _hpPresets = [];
    readonly List<NoisePreset> _noisePresets = [];
    readonly List<CompressorPreset> _compPresets = [];
    readonly List<EffectRackPreset> _rackPresets = [];
    readonly Slider[] _bandSliders = new Slider[EqualizerEffect.BandCount];
    readonly TextBlock[] _bandValues = new TextBlock[EqualizerEffect.BandCount];
    StoredChain _stored = new();
    EqPreset? _selected;
    NormalizePreset? _normSelected;
    HighPassPreset? _hpSelected;
    NoisePreset? _noiseSelected;
    CompressorPreset? _compSelected;
    EffectRackPreset? _rackSelected;
    Border? _dragCard;
    int _dropIndex = -1;
    Border? _marked;
    Brush? _markedBrush;
    Thickness _markedThickness;
    bool _noiseMessageError;
    bool _ready;
    bool _dirty;
    bool _applying;
    bool _applyingRack;
    bool _chainErrorShown;
    string? _folder;
    float[]? _noiseMono;
    int _noiseRate;
    string? _noiseFile;
    int _noiseGeneration;
    bool _syncingNoiseMenu;
    double? _noiseStart;
    double? _noiseEnd;

    public EffectRack()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => RefreshThemeBrushes();
        Loaded += (_, _) => RefreshThemeBrushes();
        NoiseWaveHost.Child = _wave;
        _wave.RangeChanging += Wave_Changing;
        _wave.RangeChosen += Wave_Chosen;
        EqGrip.Tag = EqCard;
        CompGrip.Tag = CompCard;
        NormGrip.Tag = NormCard;
        HpGrip.Tag = HpCard;
        _chain.SetOrder([_noise, _hp, _eq, _comp, _norm]);
        BuildEqSliders();
        LoadPresets();
        LoadNormPresets();
        LoadHpPresets();
        LoadNoisePresets();
        LoadCompPresets();
        LoadRackPresets();
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
            InvalidateProcessedPreview();
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
            InvalidateProcessedPreview();
        };
        NormSlider.PointerReleased += (_, _) => SaveChain();
        NormSlider.LostFocus += (_, _) => SaveChain();
        NoiseToggle.Toggled += NoiseToggle_Toggled;
        NoiseReduction.ValueChanged += (_, _) =>
        {
            if (_applying)
                return;
            _noise.ReductionDb = NoiseReduction.Value;
            UpdateNoiseChrome();
            InvalidateProcessedPreview();
        };
        NoiseReduction.PointerReleased += (_, _) => SaveChain();
        NoiseReduction.LostFocus += (_, _) => SaveChain();
        CompToggle.Toggled += CompToggle_Toggled;
        CompRatio.ValueChanged += (_, _) =>
        {
            if (_applying)
                return;
            _comp.Ratio = CompRatio.Value;
            UpdateCompChrome();
            InvalidateProcessedPreview();
        };
        CompRatio.PointerReleased += (_, _) => SaveChain();
        CompRatio.LostFocus += (_, _) => SaveChain();
        CompReduction.ValueChanged += (_, _) =>
        {
            if (_applying)
                return;
            _comp.ReductionDb = CompReduction.Value;
            UpdateCompChrome();
            InvalidateProcessedPreview();
        };
        CompReduction.PointerReleased += (_, _) => SaveChain();
        CompReduction.LostFocus += (_, _) => SaveChain();
        _ready = true;
        UpdateChrome();
        UpdateNormChrome();
        UpdateHpChrome();
        UpdateNoiseChrome();
        UpdateCompChrome();
        BindPreviewPlayer();
    }

    public bool NeedsNoiseRange => _noise.Enabled && !_noise.HasProfile;

    public string? NoiseApplyNote
    {
        get
        {
            if (!_noise.Enabled || !_noise.HasProfile || _noiseStart is not double start || _noiseEnd is not double end)
                return null;
            var seconds = Math.Abs(end - start).ToString("0.00", CultureInfo.InvariantCulture);
            return $"ノイズサプレッサーは、選んだ {seconds}秒の無音で抑えます。";
        }
    }

    internal void SetColumnDrag(bool dragging) => _wave.SetColumnDrag(dragging);

    public void SetFolder(string? folder)
    {
        var next = string.IsNullOrWhiteSpace(folder) ? null : Path.GetFullPath(folder);
        if (string.Equals(_folder, next, StringComparison.OrdinalIgnoreCase))
            return;
        StopListening();
        _folder = next;
        _noiseGeneration++;
        _noise.ClearProfile();
        _noiseMono = null;
        _noiseFile = null;
        _noiseStart = null;
        _noiseEnd = null;
        _wave.Clear();
        FillNoiseMenu();
        NoiseWaveHost.Visibility = Visibility.Collapsed;
        NoiseDuration.Text = "";
        NoiseMessage.Text = "";
        if (next == null)
            return;
        var capture = NoiseCaptureStore.Find(next);
        if (capture == null || capture.File.Length == 0)
            return;
        var path = NoiseSources.Resolve(next, capture.File);
        if (path == null)
            return;
        _ = LoadNoiseFileAsync(new NoiseSource(capture.File, path, true), capture.Start, capture.End);
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
        ApplyEffectSnapshot(_stored.Effects);
        SyncRackPresetSelection();
    }

    void ApplyEffectSnapshot(IReadOnlyList<StoredEffect> entries)
    {
        var order = new List<AudioEffect>();
        var seen = new HashSet<string>();
        foreach (var entry in entries)
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
                HpToggle.IsOn = entry.Enabled;
                _hpSelected = FindHpPreset(entry.Preset) ?? FindMatchingHp(_hp.Frequency, _hp.Slope);
                ShowHighPass();
                order.Add(_hp);
            }
            else if (entry.Kind == NoiseSuppressorEffect.KindId && seen.Add(entry.Kind))
            {
                _noise.Enabled = entry.Enabled;
                _noise.ReductionDb = entry.Reduction;
                _noiseSelected = FindNoisePreset(entry.Preset) ?? FindMatchingNoise(_noise.ReductionDb);
                ShowNoise();
            }
            else if (entry.Kind == CompressorEffect.KindId && seen.Add(entry.Kind))
            {
                _comp.Enabled = entry.Enabled;
                _comp.Ratio = entry.Ratio;
                _comp.ReductionDb = entry.Reduction;
                _compSelected = FindCompPreset(entry.Preset) ?? FindMatchingComp(_comp.Ratio, _comp.ReductionDb);
                ShowCompressor();
                order.Add(_comp);
            }
        }

        if (seen.Add(HighPassEffect.KindId))
            order.Insert(0, _hp);
        if (seen.Add(EqualizerEffect.KindId))
            order.Add(_eq);
        if (seen.Add(CompressorEffect.KindId))
        {
            var normAt = order.FindIndex(effect => ReferenceEquals(effect, _norm));
            if (normAt >= 0)
                order.Insert(normAt, _comp);
            else
                order.Add(_comp);
        }

        if (seen.Add(NormalizeEffect.KindId))
            order.Add(_norm);
        order.RemoveAll(effect => ReferenceEquals(effect, _noise));
        order.Insert(0, _noise);
        _chain.SetOrder(order);
        LayoutCards();
        UpdateChrome();
        UpdateDirty();
        UpdateNormDirty();
        UpdateHpChrome();
        UpdateNoiseChrome();
        UpdateCompChrome();
        if (!_applyingRack)
            UpdateRackDirty();
    }

    void LoadRackPresets()
    {
        _rackPresets.Clear();
        _rackPresets.AddRange(EffectRackPresetStore.Load());
        RebuildRackPresetMenu();
    }

    void RebuildRackPresetMenu() =>
        PresetUi.Fill(
            RackPresetFlyout,
            "rack-preset",
            EffectRackPreset.BuiltIns,
            _rackPresets,
            _rackSelected,
            SelectRackPreset,
            separator: true);

    void SelectRackPreset(EffectRackPreset preset)
    {
        _applyingRack = true;
        _rackSelected = preset;
        ApplyEffectSnapshot(preset.Effects);
        _applyingRack = false;
        UpdateRackDirty();
        InvalidateProcessedPreview();
        SaveChain();
    }

    void SyncRackPresetSelection()
    {
        var snapshot = CaptureCurrentSnapshot();
        _rackSelected = EffectRackPreset.BuiltIns.FirstOrDefault(preset => preset.Matches(snapshot))
            ?? _rackPresets.FirstOrDefault(preset => preset.Matches(snapshot));
        UpdateRackDirty();
        RebuildRackPresetMenu();
    }

    List<StoredEffect> CaptureCurrentSnapshot() => BuildStoredEffectList();

    void UpdateRackDirty()
    {
        if (_applyingRack)
            return;
        var snapshot = CaptureCurrentSnapshot();
        var dirty = _rackSelected != null && !_rackSelected.Matches(snapshot);
        PresetUi.MarkDirty(
            RackPresetFlyout,
            RackPresetNameText,
            RackPresetDirtyMark,
            _rackSelected,
            dirty,
            EffectRackPreset.CustomName);
    }

    void SaveRackPreset_Click(object sender, RoutedEventArgs e)
    {
        _ = SaveRackPresetAsync();
    }

    async Task SaveRackPresetAsync() =>
        await SaveStoredPresetAsync(
            "現在のエフェクト設定を保存",
            "エフェクト全体の設定につける名前を入力してください。",
            name => $"「{name}」は既にあります。いまの設定で置き換えます。",
            _rackSelected,
            _rackPresets,
            raw => PresetUi.ReservedNameError(raw, EffectRackPreset.IsReservedName),
            name => new EffectRackPreset { Name = name },
            target => target.Effects = CaptureCurrentSnapshot().Select(effect => effect.Clone()).ToList(),
            () => EffectRackPresetStore.Save(_rackPresets),
            target =>
            {
                RebuildRackPresetMenu();
                _rackSelected = target;
                UpdateRackDirty();
            });

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
            if (ReferenceEquals(effect, _noise))
                EffectList.Children.Add(NoiseCard);
            else if (ReferenceEquals(effect, _hp))
                EffectList.Children.Add(HpCard);
            else if (ReferenceEquals(effect, _eq))
                EffectList.Children.Add(EqCard);
            else if (ReferenceEquals(effect, _comp))
                EffectList.Children.Add(CompCard);
            else if (ReferenceEquals(effect, _norm))
                EffectList.Children.Add(NormCard);
        }
    }

    void EqToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _eq.Enabled = EqToggle.IsOn;
        UpdateChrome();
        InvalidateProcessedPreview();
        SaveChain();
    }

    void NormToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _norm.Enabled = NormToggle.IsOn;
        UpdateNormChrome();
        InvalidateProcessedPreview();
        SaveChain();
    }

    void HpToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _hp.Enabled = HpToggle.IsOn;
        UpdateHpChrome();
        InvalidateProcessedPreview();
        SaveChain();
    }

    void NoiseToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _noise.Enabled = NoiseToggle.IsOn;
        UpdateNoiseChrome();
        InvalidateProcessedPreview();
        SaveChain();
    }

    void ShowNoise()
    {
        _applying = true;
        NoiseToggle.IsOn = _noise.Enabled;
        NoiseReduction.Value = _noise.ReductionDb;
        _applying = false;
    }

    void UpdateNoiseChrome()
    {
        NoiseBody.Opacity = NoiseToggle.IsOn ? 1 : 0.5;
        NoiseReductionValue.Text = _noise.ReductionDb.ToString("0.0", CultureInfo.InvariantCulture) + " dB";
        UpdateNoiseDirty();
    }

    void LoadNoisePresets()
    {
        _noisePresets.Clear();
        _noisePresets.AddRange(NoisePresetStore.Load());
        var standard = NoisePreset.BuiltIns.First(preset => preset.Matches(NoiseSuppressorEffect.DefaultReductionDb));
        _noiseSelected = standard;
        _noise.ReductionDb = standard.Reduction;
        ShowNoise();
        RebuildNoisePresetMenu();
        UpdateNoiseDirty();
    }

    void RebuildNoisePresetMenu() =>
        PresetUi.Fill(
            NoisePresetFlyout,
            "noise-preset",
            NoisePreset.BuiltIns,
            _noisePresets,
            _noiseSelected,
            SelectNoisePreset,
            separator: true);

    void SelectNoisePreset(NoisePreset preset)
    {
        _noiseSelected = preset;
        _noise.ReductionDb = preset.Reduction;
        ShowNoise();
        UpdateNoiseChrome();
        InvalidateProcessedPreview();
        SaveChain();
    }

    void UpdateNoiseDirty()
    {
        var dirty = _noiseSelected != null && !_noiseSelected.Matches(_noise.ReductionDb);
        PresetUi.MarkDirty(
            NoisePresetFlyout,
            NoisePresetNameText,
            NoisePresetDirtyMark,
            _noiseSelected,
            dirty,
            NoisePreset.CustomName);
    }

    NoisePreset? FindNoisePreset(string name)
    {
        if (name.Length == 0)
            return null;
        return NoisePreset.BuiltIns.FirstOrDefault(preset => preset.Name == name)
            ?? _noisePresets.FirstOrDefault(preset => preset.Name == name);
    }

    NoisePreset? FindMatchingNoise(double db) =>
        NoisePreset.BuiltIns.FirstOrDefault(preset => preset.Matches(db))
        ?? _noisePresets.FirstOrDefault(preset => preset.Matches(db));

    void SaveNoisePreset_Click(object sender, RoutedEventArgs e)
    {
        _ = SaveNoisePresetAsync();
    }

    async Task SaveNoisePresetAsync() =>
        await SaveStoredPresetAsync(
            "現在の抑える量を保存",
            "この抑える量につける名前を入力してください。",
            name => $"「{name}」は既にあります。いまの抑える量で置き換えます。",
            _noiseSelected,
            _noisePresets,
            raw => PresetUi.ReservedNameError(raw, NoisePreset.IsReservedName),
            name => new NoisePreset { Name = name },
            target => target.Reduction = _noise.ReductionDb,
            () => NoisePresetStore.Save(_noisePresets),
            target =>
            {
                RebuildNoisePresetMenu();
                SelectNoisePreset(target);
            });

    void FillNoiseMenu()
    {
        _syncingNoiseMenu = true;
        try
        {
            NoiseFileMenu.Items.Clear();
            NoiseFileMenu.SelectedItem = null;
            if (_folder != null)
            {
                foreach (var file in NoiseSources.ListOriginal(_folder))
                    NoiseFileMenu.Items.Add(file.Name);
            }

            var hasFiles = NoiseFileMenu.Items.Count > 0;
            NoiseFileMenu.IsEnabled = hasFiles;
            NoiseFileMenu.PlaceholderText = hasFiles ? "原音を選ぶ" : "WAV がありません";
            ToolTipService.SetToolTip(NoiseFileMenu, "original フォルダの WAV を選びます");
            FillListenMenus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            NoiseFileMenu.IsEnabled = false;
            NoiseFileMenu.PlaceholderText = "WAV がありません";
            NoiseMessage.Text = ex.Message;
            PaintNoiseMessage(error: true);
            FillListenMenus();
        }
        finally
        {
            _syncingNoiseMenu = false;
        }
    }

    void ShowNoiseFile(string? name)
    {
        _syncingNoiseMenu = true;
        try
        {
            if (string.IsNullOrEmpty(name))
            {
                NoiseFileMenu.SelectedItem = null;
                ToolTipService.SetToolTip(NoiseFileMenu, "original フォルダの WAV を選びます");
                return;
            }

            string? match = null;
            foreach (var item in NoiseFileMenu.Items)
            {
                if (item is string text && string.Equals(text, name, StringComparison.OrdinalIgnoreCase))
                {
                    match = text;
                    break;
                }
            }

            if (match == null)
            {
                NoiseFileMenu.Items.Add(name);
                match = name;
                NoiseFileMenu.IsEnabled = true;
            }

            NoiseFileMenu.SelectedItem = match;
            ToolTipService.SetToolTip(NoiseFileMenu, name);
        }
        finally
        {
            _syncingNoiseMenu = false;
        }
    }

    void NoiseFile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingNoiseMenu || _folder == null || NoiseFileMenu.SelectedItem is not string name)
            return;
        _ = OpenNoiseFileAsync(name);
    }

    async Task OpenNoiseFileAsync(string name)
    {
        var folder = _folder;
        if (folder == null)
            return;
        var path = NoiseSources.Resolve(folder, name);
        if (path == null)
            return;
        var source = new NoiseSource(name, path, true);
        if (string.Equals(source.Name, _noiseFile, StringComparison.OrdinalIgnoreCase)
            && _noiseStart is double keptStart
            && _noiseEnd is double keptEnd)
        {
            await LoadNoiseFileAsync(source, keptStart, keptEnd);
            return;
        }

        await LoadNoiseFileAsync(source, null, null);
    }

    async Task LoadNoiseFileAsync(NoiseSource source, double? start, double? end)
    {
        var generation = ++_noiseGeneration;
        var folder = _folder;
        NoiseMessage.Text = "読み込んでいます。";
        PaintNoiseMessage(error: false);
        WavAudio audio;
        try
        {
            audio = await Task.Run(() => WavAudio.Read(source.Path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (generation != _noiseGeneration)
                return;
            NoiseMessage.Text = ex.Message;
            PaintNoiseMessage(error: true);
            ShowNoiseFile(_noiseFile);
            return;
        }

        if (generation != _noiseGeneration || !string.Equals(folder, _folder, StringComparison.OrdinalIgnoreCase))
            return;
        _noiseMono = NoiseSuppressorEffect.Downmix(audio.Samples, audio.Channels);
        _noiseRate = audio.SampleRate;
        _noiseFile = source.Name;
        _noise.ClearProfile();
        InvalidateProcessedPreview();
        _noiseStart = null;
        _noiseEnd = null;
        ShowNoiseFile(source.Name);
        NoiseWaveHost.Visibility = Visibility.Visible;
        _wave.SetAudio(_noiseMono, audio.SampleRate);
        if (start is double rangeStart && end is double rangeEnd)
        {
            TryCommit(rangeStart, rangeEnd, save: false);
            return;
        }

        _wave.ClearRange();
        NoiseDuration.Text = "";
        NoiseMessage.Text = "ドラッグして、無音の範囲を選んでください。";
        PaintNoiseMessage(error: false);
        if (folder != null)
        {
            try
            {
                NoiseCaptureStore.Remove(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await TellAsync("状態を保存できませんでした", ex.Message);
            }
        }
    }

    void Wave_Changing(object? sender, NoiseRangeEventArgs e)
    {
        NoiseMessage.Text = "";
        NoiseDuration.Text = FormatSelection(e.Start, e.End);
    }

    void Wave_Chosen(object? sender, NoiseRangeEventArgs e)
    {
        TryCommit(e.Start, e.End, save: true);
    }

    void TryCommit(double start, double end, bool save)
    {
        if (_noiseMono == null || _noiseFile == null)
            return;
        var message = NoiseSuppressorEffect.RangeMessage(_noiseMono, _noiseRate, start, end);
        if (message != null)
        {
            NoiseMessage.Text = message;
            PaintNoiseMessage(error: true);
            if (_noiseStart is double keptStart && _noiseEnd is double keptEnd)
            {
                _wave.SetRange(keptStart, keptEnd);
                NoiseDuration.Text = FormatSelection(keptStart, keptEnd);
            }
            else
            {
                _wave.ClearRange();
                NoiseDuration.Text = FormatSelection(start, end);
            }

            return;
        }

        NoiseProfile profile;
        try
        {
            profile = NoiseSuppressorEffect.BuildProfile(_noiseMono, _noiseRate, start, end);
        }
        catch (InvalidDataException ex)
        {
            NoiseMessage.Text = ex.Message;
            PaintNoiseMessage(error: true);
            return;
        }

        if (end < start)
            (start, end) = (end, start);
        _noise.SetProfile(profile);
        InvalidateProcessedPreview();
        _noiseStart = start;
        _noiseEnd = end;
        _wave.SetRange(start, end);
        NoiseDuration.Text = FormatSelection(start, end);
        NoiseMessage.Text = "";
        if (!save || _folder == null)
            return;
        try
        {
            NoiseCaptureStore.Save(new NoiseCapture
            {
                Folder = _folder,
                File = _noiseFile,
                Start = start,
                End = end,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = TellAsync("状態を保存できませんでした", ex.Message);
        }
    }

    static string FormatSelection(double start, double end) =>
        Math.Abs(end - start).ToString("0.00", CultureInfo.InvariantCulture) + "秒";

    void PaintNoiseMessage(bool error)
    {
        _noiseMessageError = error;
        var key = error ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush";
        if (AppearanceTheme.Brush(AppearanceTheme.Resolve(this), key) is Brush paint)
            NoiseMessage.Foreground = paint;
    }

    void RefreshThemeBrushes()
    {
        PaintNoiseMessage(_noiseMessageError);
        if (DryStatus.Visibility == Visibility.Visible)
            ShowListenStatus(processed: false, DryStatus.Text, error: true);
        if (WetStatus.Visibility == Visibility.Visible)
            ShowListenStatus(processed: true, WetStatus.Text, error: true);
    }

    void HpSlope_Checked(object sender, RoutedEventArgs e)
    {
        if (_applying)
            return;
        var slope = HpSlope24.IsChecked == true ? HighPassEffect.Slope24 : HighPassEffect.Slope12;
        if (slope == _hp.Slope)
            return;
        _hp.Slope = slope;
        UpdateHpDirty();
        InvalidateProcessedPreview();
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
        UpdateHpDirty();
    }

    void LoadHpPresets()
    {
        _hpPresets.Clear();
        _hpPresets.AddRange(HighPassPresetStore.Load());
        var standard = HighPassPreset.BuiltIns.First(preset =>
            preset.Matches(HighPassEffect.DefaultHz, HighPassEffect.DefaultSlope));
        _hpSelected = standard;
        _hp.Frequency = standard.Frequency;
        _hp.Slope = standard.Slope;
        ShowHighPass();
        RebuildHpPresetMenu();
        UpdateHpDirty();
    }

    void UpdateNormChrome()
    {
        NormBody.Opacity = NormToggle.IsOn ? 1 : 0.5;
        NormValue.Text = FormatDb(_norm.TargetDb);
        UpdateNormDirty();
    }

    void CompToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _comp.Enabled = CompToggle.IsOn;
        UpdateCompChrome();
        InvalidateProcessedPreview();
        SaveChain();
    }

    void ShowCompressor()
    {
        _applying = true;
        CompToggle.IsOn = _comp.Enabled;
        CompRatio.Value = _comp.Ratio;
        CompReduction.Value = _comp.ReductionDb;
        _applying = false;
    }

    void UpdateCompChrome()
    {
        CompBody.Opacity = CompToggle.IsOn ? 1 : 0.5;
        CompRatioValue.Text = _comp.Ratio.ToString("0.0", CultureInfo.InvariantCulture) + ":1";
        CompReductionValue.Text = _comp.ReductionDb.ToString("0.0", CultureInfo.InvariantCulture) + " dB";
        UpdateCompDirty();
    }

    void LoadCompPresets()
    {
        _compPresets.Clear();
        _compPresets.AddRange(CompressorPresetStore.Load());
        var standard = CompressorPreset.BuiltIns.First(preset =>
            preset.Matches(CompressorEffect.DefaultRatio, CompressorEffect.DefaultReductionDb));
        _compSelected = standard;
        _comp.Ratio = standard.Ratio;
        _comp.ReductionDb = standard.Reduction;
        ShowCompressor();
        RebuildCompPresetMenu();
        UpdateCompDirty();
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
        if (from <= 0 || to < 0 || from == to)
            return;
        if (to < 1)
            to = 1;
        _chain.Move(from, to);
        PinNoise();
        LayoutCards();
        InvalidateProcessedPreview();
        SaveChain();
    }

    void PinNoise()
    {
        var effects = _chain.Effects.ToList();
        if (effects.Count == 0 || ReferenceEquals(effects[0], _noise))
            return;
        effects.Remove(_noise);
        effects.Insert(0, _noise);
        _chain.SetOrder(effects);
    }

    int IndexAt(Windows.Foundation.Point point)
    {
        var count = EffectList.Children.Count;
        var index = Math.Max(0, count - 1);
        for (var i = 0; i < count; i++)
        {
            if (EffectList.Children[i] is not FrameworkElement element)
                continue;
            var top = element.TransformToVisual(EffectList).TransformPoint(default).Y;
            if (point.Y < top + element.ActualHeight * 0.5)
            {
                index = i;
                break;
            }
        }

        if (count > 1 && index < 1)
            return 1;
        return index;
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
        border.BorderBrush = AppearanceTheme.Brush(AppearanceTheme.Resolve(this), "AccentFillColorDefaultBrush") ?? border.BorderBrush;
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
        InvalidateProcessedPreview();
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

    void RebuildPresetMenu() =>
        PresetUi.Fill(
            PresetFlyout,
            "eq-preset",
            EqPreset.BuiltIns,
            _userPresets,
            _selected,
            SelectPreset,
            separator: true);

    void SelectPreset(EqPreset preset)
    {
        _selected = preset;
        _eq.SetGains(preset.Gains);
        ShowGains();
        UpdateChrome();
        UpdateDirty();
        InvalidateProcessedPreview();
        SaveChain();
    }

    void UpdateDirty()
    {
        _dirty = _selected != null && !_eq.Matches(_selected.Gains);
        PresetUi.MarkDirty(PresetFlyout, PresetNameText, PresetDirtyMark, _selected, _dirty, EqPreset.CustomName);
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

    List<StoredEffect> BuildStoredEffectList()
    {
        var list = new List<StoredEffect>();
        foreach (var effect in _chain.Effects)
        {
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
                    Preset = _hpSelected?.Name ?? "",
                    Frequency = _hp.Frequency,
                    Slope = _hp.Slope,
                });
            }
            else if (ReferenceEquals(effect, _noise))
            {
                list.Add(new StoredEffect
                {
                    Kind = NoiseSuppressorEffect.KindId,
                    Enabled = _noise.Enabled,
                    Preset = _noiseSelected?.Name ?? "",
                    Reduction = _noise.ReductionDb,
                });
            }
            else if (ReferenceEquals(effect, _comp))
            {
                list.Add(new StoredEffect
                {
                    Kind = CompressorEffect.KindId,
                    Enabled = _comp.Enabled,
                    Preset = _compSelected?.Name ?? "",
                    Ratio = _comp.Ratio,
                    Reduction = _comp.ReductionDb,
                });
            }
        }

        return list;
    }

    void SaveChain()
    {
        if (!_ready)
            return;
        var known = new HashSet<string>();
        var list = BuildStoredEffectList();
        foreach (var entry in list)
            known.Add(entry.Kind);

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

        UpdateRackDirty();
    }

    void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        _ = SavePresetAsync();
    }

    async Task SavePresetAsync() =>
        await SaveStoredPresetAsync(
            "現在の設定を保存",
            "この設定につける名前を入力してください。",
            name => $"「{name}」は既にあります。いまの設定で置き換えます。",
            _selected,
            _userPresets,
            raw => PresetUi.ReservedNameError(raw, EqPreset.IsReservedName),
            name => new EqPreset { Name = name },
            target => target.Gains = _eq.CopyGains(),
            () => EqPresetStore.Save(_userPresets),
            target =>
            {
                RebuildPresetMenu();
                SelectPreset(target);
            });

    void RebuildHpPresetMenu() =>
        PresetUi.Fill(
            HpPresetFlyout,
            "hp-preset",
            HighPassPreset.BuiltIns,
            _hpPresets,
            _hpSelected,
            SelectHpPreset,
            separator: true);

    void SelectHpPreset(HighPassPreset preset)
    {
        _hpSelected = preset;
        _hp.Frequency = preset.Frequency;
        _hp.Slope = preset.Slope;
        ShowHighPass();
        UpdateHpChrome();
        InvalidateProcessedPreview();
        SaveChain();
    }

    void UpdateHpDirty()
    {
        var dirty = _hpSelected != null && !_hpSelected.Matches(_hp.Frequency, _hp.Slope);
        PresetUi.MarkDirty(HpPresetFlyout, HpPresetNameText, HpPresetDirtyMark, _hpSelected, dirty, HighPassPreset.CustomName);
    }

    HighPassPreset? FindHpPreset(string name)
    {
        if (name.Length == 0)
            return null;
        return HighPassPreset.BuiltIns.FirstOrDefault(preset => preset.Name == name)
            ?? _hpPresets.FirstOrDefault(preset => preset.Name == name);
    }

    HighPassPreset? FindMatchingHp(double hz, int slope) =>
        HighPassPreset.BuiltIns.FirstOrDefault(preset => preset.Matches(hz, slope))
        ?? _hpPresets.FirstOrDefault(preset => preset.Matches(hz, slope));

    void SaveHpPreset_Click(object sender, RoutedEventArgs e)
    {
        _ = SaveHpPresetAsync();
    }

    async Task SaveHpPresetAsync() =>
        await SaveStoredPresetAsync(
            "現在の設定を保存",
            "この設定につける名前を入力してください。",
            name => $"「{name}」は既にあります。いまの設定で置き換えます。",
            _hpSelected,
            _hpPresets,
            raw => PresetUi.ReservedNameError(raw, HighPassPreset.IsReservedName),
            name => new HighPassPreset { Name = name },
            target =>
            {
                target.Frequency = _hp.Frequency;
                target.Slope = _hp.Slope;
            },
            () => HighPassPresetStore.Save(_hpPresets),
            target =>
            {
                RebuildHpPresetMenu();
                SelectHpPreset(target);
            });

    void RebuildCompPresetMenu() =>
        PresetUi.Fill(
            CompPresetFlyout,
            "comp-preset",
            CompressorPreset.BuiltIns,
            _compPresets,
            _compSelected,
            SelectCompPreset,
            separator: true);

    void SelectCompPreset(CompressorPreset preset)
    {
        _compSelected = preset;
        _comp.Ratio = preset.Ratio;
        _comp.ReductionDb = preset.Reduction;
        ShowCompressor();
        UpdateCompChrome();
        InvalidateProcessedPreview();
        SaveChain();
    }

    void UpdateCompDirty()
    {
        var dirty = _compSelected != null && !_compSelected.Matches(_comp.Ratio, _comp.ReductionDb);
        PresetUi.MarkDirty(
            CompPresetFlyout,
            CompPresetNameText,
            CompPresetDirtyMark,
            _compSelected,
            dirty,
            CompressorPreset.CustomName);
    }

    CompressorPreset? FindCompPreset(string name)
    {
        if (name.Length == 0)
            return null;
        return CompressorPreset.BuiltIns.FirstOrDefault(preset => preset.Name == name)
            ?? _compPresets.FirstOrDefault(preset => preset.Name == name);
    }

    CompressorPreset? FindMatchingComp(double ratio, double reduction) =>
        CompressorPreset.BuiltIns.FirstOrDefault(preset => preset.Matches(ratio, reduction))
        ?? _compPresets.FirstOrDefault(preset => preset.Matches(ratio, reduction));

    void SaveCompPreset_Click(object sender, RoutedEventArgs e)
    {
        _ = SaveCompPresetAsync();
    }

    async Task SaveCompPresetAsync() =>
        await SaveStoredPresetAsync(
            "現在の設定を保存",
            "この設定につける名前を入力してください。",
            name => $"「{name}」は既にあります。いまの設定で置き換えます。",
            _compSelected,
            _compPresets,
            raw => PresetUi.ReservedNameError(raw, CompressorPreset.IsReservedName),
            name => new CompressorPreset { Name = name },
            target =>
            {
                target.Ratio = _comp.Ratio;
                target.Reduction = _comp.ReductionDb;
            },
            () => CompressorPresetStore.Save(_compPresets),
            target =>
            {
                RebuildCompPresetMenu();
                SelectCompPreset(target);
            });

    void RebuildNormPresetMenu() =>
        PresetUi.Fill(
            NormPresetFlyout,
            "norm-preset",
            NormalizePreset.BuiltIns,
            _normPresets,
            _normSelected,
            SelectNormPreset,
            separator: true);

    void SelectNormPreset(NormalizePreset preset)
    {
        _normSelected = preset;
        _norm.TargetDb = preset.Target;
        _applying = true;
        NormSlider.Value = _norm.TargetDb;
        _applying = false;
        UpdateNormChrome();
        InvalidateProcessedPreview();
        SaveChain();
    }

    void UpdateNormDirty()
    {
        var dirty = _normSelected != null && !_normSelected.Matches(_norm.TargetDb);
        PresetUi.MarkDirty(
            NormPresetFlyout,
            NormPresetNameText,
            NormPresetDirtyMark,
            _normSelected,
            dirty,
            NormalizePreset.CustomName);
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

    async Task SaveNormPresetAsync() =>
        await SaveStoredPresetAsync(
            "現在のピークを保存",
            "このピークにつける名前を入力してください。",
            name => $"「{name}」は既にあります。いまのピークで置き換えます。",
            _normSelected,
            _normPresets,
            raw => PresetUi.ReservedNameError(raw, NormalizePreset.IsReservedName),
            name => new NormalizePreset { Name = name },
            target => target.Target = _norm.TargetDb,
            () => NormalizePresetStore.Save(_normPresets),
            target =>
            {
                RebuildNormPresetMenu();
                SelectNormPreset(target);
            });

    async Task SaveStoredPresetAsync<T>(
        string title,
        string prompt,
        Func<string, string> overwrite,
        T? selected,
        IList<T> presets,
        Func<string, string?> validate,
        Func<string, T> create,
        Action<T> copy,
        Action save,
        Action<T> after) where T : class, INamedPreset
    {
        var target = await PresetUi.AcceptSaveAsync(
            XamlRoot,
            title,
            prompt,
            selected,
            presets,
            validate,
            overwrite,
            create,
            copy);
        if (target == null)
            return;
        try
        {
            save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await TellAsync("プリセットを保存できませんでした", ex.Message);
            return;
        }

        after(target);
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

    internal EffectLibrary CaptureLibrary() =>
        new()
        {
            Chain = BuildStoredEffectList().Select(effect => effect.Clone()).ToList(),
            EqPresets = _userPresets.ToList(),
            NormalizePresets = _normPresets.ToList(),
            HighPassPresets = _hpPresets.ToList(),
            NoisePresets = _noisePresets.ToList(),
            CompressorPresets = _compPresets.ToList(),
            RackPresets = _rackPresets.ToList(),
        };

    internal void ReplaceLibrary(EffectLibrary library)
    {
        var eq = EqPresetStore.Normalize(library.EqPresets);
        var norm = NormalizePresetStore.Normalize(library.NormalizePresets);
        var hp = HighPassPresetStore.Normalize(library.HighPassPresets);
        var noise = NoisePresetStore.Normalize(library.NoisePresets);
        var comp = CompressorPresetStore.Normalize(library.CompressorPresets);
        var rack = EffectRackPresetStore.Normalize(library.RackPresets);
        var chain = EffectRackPresetStore.NormalizeEffects(library.Chain);
        EqPresetStore.Save(eq);
        NormalizePresetStore.Save(norm);
        HighPassPresetStore.Save(hp);
        NoisePresetStore.Save(noise);
        CompressorPresetStore.Save(comp);
        EffectRackPresetStore.Save(rack);

        _userPresets.Clear();
        _userPresets.AddRange(eq);
        _normPresets.Clear();
        _normPresets.AddRange(norm);
        _hpPresets.Clear();
        _hpPresets.AddRange(hp);
        _noisePresets.Clear();
        _noisePresets.AddRange(noise);
        _compPresets.Clear();
        _compPresets.AddRange(comp);
        _rackPresets.Clear();
        _rackPresets.AddRange(rack);
        RebuildPresetMenu();
        RebuildNormPresetMenu();
        RebuildHpPresetMenu();
        RebuildNoisePresetMenu();
        RebuildCompPresetMenu();
        RebuildRackPresetMenu();
        ApplyEffectSnapshot(chain);
        SyncRackPresetSelection();
        SaveChain();
        InvalidateProcessedPreview();
    }
}
