using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Otoasobi;

public sealed partial class MainPage
{
    void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        _ = SaveSettingsAsync();
    }

    void LoadSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        _ = LoadSettingsAsync();
    }

    async Task SaveSettingsAsync()
    {
        var folder = _folder;
        if (XamlRoot == null || folder == null)
            return;

        var box = new TextBox { PlaceholderText = "設定名" };
        var error = DialogErrorLine();
        var panel = new StackPanel { Spacing = 8, MinWidth = 360 };
        panel.Children.Add(new TextBlock
        {
            Text = "いまの計算方法、スライダー、プリセットを、このフォルダに保存します。原音修正の設定も含みます。",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(box);
        panel.Children.Add(error);
        panel.Children.Add(HintText("保存先"));
        panel.Children.Add(PathText(folder));
        var dialog = new ContentDialog
        {
            Title = "設定を保存",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        dialog.Opened += (_, _) => box.Focus(FocusState.Programmatic);
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var message = OtoSaveStore.NameError(box.Text);
            if (message == null)
                return;
            args.Cancel = true;
            error.Text = message;
            error.Visibility = Visibility.Visible;
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var name = box.Text.Trim();
        if (File.Exists(OtoSaveStore.FilePathFor(folder, name)))
        {
            var confirm = new ContentDialog
            {
                Title = "設定を上書きしますか？",
                Content = $"「{name}」は既にあります。いまの設定で置き換えます。",
                PrimaryButtonText = "上書きする",
                CloseButtonText = "キャンセル",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                return;
        }

        try
        {
            var file = CaptureSettings(includeLibraries: true);
            file.Name = name;
            OtoSaveStore.WriteNamed(folder, file);
            FlushSession(force: true);
            ShowNotice("設定を保存しました", OtoSaveStore.FilePathFor(folder, name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowNotice("設定を保存できませんでした", ex.Message);
        }
    }

    async Task LoadSettingsAsync()
    {
        var folder = _folder;
        if (XamlRoot == null || folder == null)
            return;
        while (true)
        {
            var pick = await PickSaveAsync(folder);
            if (pick == null)
                return;
            if (pick.Value.Delete)
            {
                if (!await ConfirmDeleteAsync(pick.Value.Name))
                    continue;
                try
                {
                    OtoSaveStore.Delete(folder, pick.Value.Name);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    ShowNotice("設定を削除できませんでした", ex.Message);
                    return;
                }

                continue;
            }

            try
            {
                var file = OtoSaveStore.Read(pick.Value.Path);
                ApplyLoaded(file);
                ShowNotice("設定を読み込みました", file.Name + "　" + OtoSaveStore.ModeLabel(file.Mode));
            }
            catch (InvalidDataException ex)
            {
                ShowNotice("設定を読み込めませんでした", ex.Message);
            }
            catch (JsonException)
            {
                ShowNotice("設定を読み込めませんでした", "ファイルの内容を読めませんでした。");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowNotice("設定を読み込めませんでした", ex.Message);
            }

            return;
        }
    }

    async Task<(bool Delete, string Name, string Path)?> PickSaveAsync(string folder)
    {
        var entries = OtoSaveStore.List(folder);
        var options = new StackPanel { Spacing = 2 };
        var empty = new TextBlock
        {
            Text = "このフォルダには、まだ設定ファイルがありません。「設定を保存」で作れます。",
            TextWrapping = TextWrapping.Wrap,
            Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed,
        };
        var scroller = new ScrollViewer
        {
            MaxHeight = 320,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
            Content = options,
            Visibility = entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible,
        };
        (bool Delete, string Name, string Path)? chosen = null;
        OtoSaveSummary? selected = null;
        var dialog = new ContentDialog
        {
            Title = "設定を読み込む",
            PrimaryButtonText = "読み込む",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false,
            XamlRoot = XamlRoot,
        };
        var delete = new Button
        {
            Content = "削除",
            IsEnabled = false,
            Style = (Style)Resources["ToolbarButtonStyle"],
        };
        delete.Click += (_, _) =>
        {
            if (selected == null)
                return;
            chosen = (true, selected.Name, selected.Path);
            dialog.Hide();
        };
        var open = new Button
        {
            Content = "フォルダを開く",
            Style = (Style)Resources["ToolbarButtonStyle"],
        };
        open.Click += (_, _) =>
        {
            try
            {
                OtoSaveStore.OpenFolder(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowNotice("フォルダを開けませんでした", ex.Message);
            }
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (selected == null)
            {
                args.Cancel = true;
                return;
            }

            chosen = (false, selected.Name, selected.Path);
        };

        foreach (var summary in entries)
        {
            var item = summary;
            var body = new StackPanel { Spacing = 0 };
            body.Children.Add(new TextBlock
            {
                Text = item.Name,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            body.Children.Add(new TextBlock
            {
                Text = item.Detail,
                FontSize = 12,
                Opacity = 0.7,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var radio = new RadioButton
            {
                GroupName = "oto-save-file",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                MinHeight = 44,
                Content = body,
                Tag = item,
            };
            radio.Checked += (_, _) =>
            {
                selected = item;
                dialog.IsPrimaryButtonEnabled = true;
                dialog.DefaultButton = ContentDialogButton.Primary;
                delete.IsEnabled = true;
            };
            radio.DoubleTapped += (_, args) =>
            {
                selected = item;
                radio.IsChecked = true;
                chosen = (false, item.Name, item.Path);
                dialog.Hide();
                args.Handled = true;
            };
            options.Children.Add(radio);
        }

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        actions.Children.Add(delete);
        actions.Children.Add(open);
        var panel = new StackPanel { Spacing = 8, MinWidth = 360, MaxWidth = 520 };
        panel.Children.Add(new TextBlock
        {
            Text = "読み込むと、計算方法とスライダーが戻り、単独音、CVVC、原音修正のプリセット一覧は、このファイルの内容に置き換わります。",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(empty);
        panel.Children.Add(scroller);
        panel.Children.Add(HintText("保存先"));
        panel.Children.Add(PathText(folder));
        panel.Children.Add(actions);
        dialog.Content = panel;
        await dialog.ShowAsync();
        return chosen;
    }

    async Task<bool> ConfirmDeleteAsync(string name)
    {
        var dialog = new ContentDialog
        {
            Title = "設定を削除しますか？",
            Content = $"「{name}」を削除します。",
            PrimaryButtonText = "削除する",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    void ApplyLoaded(OtoSaveFile file)
    {
        _sessionTimer?.Stop();
        _applyingSave = true;
        try
        {
            ReplaceLibraries(file);
            ApplySliders(file);
        }
        finally
        {
            _applyingSave = false;
        }

        OnConfigChanged();
        FlushSession(force: true);
    }

    void AdoptFolder(string folder)
    {
        if (string.Equals(_folder, folder, StringComparison.OrdinalIgnoreCase))
        {
            _folder = folder;
            return;
        }

        FlushSession();
        _folder = folder;
        LoadFolderSettings();
    }

    void LoadFolderSettings()
    {
        if (_folder == null)
            return;
        _sessionLocked = false;
        var session = OtoSaveStore.TryLoadSession(_folder, out var keep);
        if (session != null)
        {
            ApplyLoaded(session);
            return;
        }

        _sessionLocked = keep;
        if (!keep)
            ApplyDefaults();
    }

    void ApplyDefaults()
    {
        _applyingSave = true;
        try
        {
            ApplySliders(new OtoSaveFile());
        }
        finally
        {
            _applyingSave = false;
        }

        OnConfigChanged();
        _sessionDirty = false;
        _sessionTimer?.Stop();
    }

    void ReplaceLibraries(OtoSaveFile file)
    {
        List<OtoPreset>? oto = null;
        List<CvvcPreset>? cvvc = null;
        if (file.Presets != null)
            oto = OtoPresetStore.Normalize(file.Presets);
        if (file.CvvcPresets != null)
            cvvc = CvvcPresetStore.Normalize(file.CvvcPresets);
        if (oto != null)
            OtoPresetStore.Save(oto);
        if (cvvc != null)
            CvvcPresetStore.Save(cvvc);
        if (oto != null)
        {
            _userPresets.Clear();
            _userPresets.AddRange(oto);
        }

        if (cvvc != null)
        {
            _cvvcPresets.Clear();
            _cvvcPresets.AddRange(cvvc);
        }

        if (oto != null || cvvc != null)
        {
            RebuildPresetMenu();
            RebuildCvvcPresetMenu();
        }

        if (file.Effects != null)
            Effects.ReplaceLibrary(file.Effects);
    }

    void ApplySliders(OtoSaveFile file)
    {
        _applyingPreset = true;
        try
        {
            var standalone = file.Standalone;
            foreach (var (key, _) in _overlapSliders)
            {
                _overlapIntent[key] = standalone.Overlap.TryGetValue(key, out var value)
                    ? value
                    : DefaultOverlapPercent;
            }

            OffsetSlider.Value = standalone.Offset;
            ConsonantSlider.Value = standalone.Consonant;
            BlankSlider.Value = standalone.Blank;
            RepositionOverlapSliders();

            var renzoku = file.Renzoku;
            RenzokuOffsetSlider.Value = renzoku.Offset;
            RenzokuConsonantSlider.Value = renzoku.Consonant;
            RenzokuBlankSlider.Value = renzoku.Blank;
            RenzokuOverlapSlider.Value = renzoku.Overlap;

            var cvvc = file.Cvvc;
            CvvcConsonantSlider.Value = cvvc.Consonant;
            CvvcBlankSlider.Value = cvvc.Blank;
            VcOffsetSlider.Value = cvvc.VcOffset;
            VcOverlapSlider.Value = cvvc.VcOverlap;
            ApplyPercents(_cvvcOverlapSliders, cvvc.Overlap, DefaultOverlapPercent);
            ApplyPercents(_vcConsonantSliders, cvvc.VcConsonant, DefaultVcConsonantPercent);
            ApplyPercents(_vcBlankSliders, cvvc.VcBlank, DefaultVcBlankPercent);

            switch (file.Mode)
            {
                case "renzoku":
                    OtoModeRenzoku.IsChecked = true;
                    break;
                case "cvvc":
                    OtoModeCvvc.IsChecked = true;
                    break;
                default:
                    OtoModeStandalone.IsChecked = true;
                    break;
            }

            PointStandalone(FindUserOto(file.StandalonePreset));
            PointRest(FindUserOto(file.RestPreset));
            PointLead(FindUserOto(file.LeadPreset));
            PointCvvc(FindUserCvvc(file.CvvcPreset));
        }
        finally
        {
            _applyingPreset = false;
        }
    }

    OtoSaveFile CaptureSettings(bool includeLibraries)
    {
        var file = new OtoSaveFile
        {
            Version = OtoSaveFile.CurrentVersion,
            Kind = OtoSaveFile.KindId,
            Mode = CurrentMode(),
            StandalonePreset = _selectedPreset?.Name ?? OtoPreset.BuiltInName,
            RestPreset = _restPreset.Name,
            CvvcPreset = _selectedCvvc?.Name ?? CvvcPreset.BuiltInName,
            LeadPreset = _leadPreset.Name,
            Standalone = new OtoSliderState
            {
                Offset = Math.Round(OffsetSlider.Value, 3),
                Consonant = Math.Round(ConsonantSlider.Value, 3),
                Blank = Math.Round(BlankSlider.Value, 3),
                Overlap = CaptureOverlap(),
            },
            Renzoku = new RenzokuSliderState
            {
                Offset = Math.Round(RenzokuOffsetSlider.Value, 3),
                Consonant = Math.Round(RenzokuConsonantSlider.Value, 3),
                Blank = Math.Round(RenzokuBlankSlider.Value, 3),
                Overlap = CvvcPresetStore.Percent(RenzokuOverlapSlider.Value, DefaultOverlapPercent),
            },
            Cvvc = new CvvcSliderState
            {
                Consonant = Math.Round(CvvcConsonantSlider.Value, 3),
                Blank = Math.Round(CvvcBlankSlider.Value, 3),
                VcOffset = Math.Round(VcOffsetSlider.Value, 3),
                VcOverlap = CvvcPresetStore.Percent(VcOverlapSlider.Value, DefaultOverlapPercent),
                Overlap = CapturePercents(_cvvcOverlapSliders, DefaultOverlapPercent),
                VcConsonant = CapturePercents(_vcConsonantSliders, DefaultVcConsonantPercent),
                VcBlank = CapturePercents(_vcBlankSliders, DefaultVcBlankPercent),
            },
        };
        if (!includeLibraries)
            return file;
        file.Presets = _userPresets.ToList();
        file.CvvcPresets = _cvvcPresets.ToList();
        file.Effects = Effects.CaptureLibrary();
        return file;
    }

    Dictionary<string, double> CaptureOverlap()
    {
        var map = new Dictionary<string, double>(ConsonantOverlap.Kinds.Length);
        foreach (var (_, key, _) in ConsonantOverlap.Kinds)
        {
            map[key] = ConsonantOverlap.Stored(
                _overlapIntent.TryGetValue(key, out var intent) ? intent : DefaultOverlapPercent);
        }

        return map;
    }

    static Dictionary<string, double> CapturePercents(Dictionary<string, Slider> sliders, double fallback)
    {
        var dest = new Dictionary<string, double>();
        CopyPercents(dest, sliders, fallback);
        return dest;
    }

    string CurrentMode()
    {
        if (OtoModeCvvc.IsChecked == true)
            return "cvvc";
        if (OtoModeRenzoku.IsChecked == true)
            return "renzoku";
        return "mono";
    }

    OtoPreset FindUserOto(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == OtoPreset.BuiltInName)
            return _builtIn;
        return _userPresets.FirstOrDefault(preset => preset.Name == name) ?? _builtIn;
    }

    CvvcPreset FindUserCvvc(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == CvvcPreset.BuiltInName)
            return _cvvcBuiltIn;
        return _cvvcPresets.FirstOrDefault(preset => preset.Name == name) ?? _cvvcBuiltIn;
    }

    void PointStandalone(OtoPreset preset)
    {
        _selectedPreset = preset;
        PresetNameText.Text = preset.Name;
        CheckRadios(PresetFlyout, preset);
    }

    void PointRest(OtoPreset preset)
    {
        _restPreset = preset;
        RestPresetNameText.Text = preset.Name;
        CheckRadios(RestPresetFlyout, preset);
    }

    void PointLead(OtoPreset preset)
    {
        _leadPreset = preset;
        CvvcLeadPresetNameText.Text = preset.Name;
        CheckRadios(CvvcLeadPresetFlyout, preset);
    }

    void PointCvvc(CvvcPreset preset)
    {
        _selectedCvvc = preset;
        CvvcPresetNameText.Text = preset.Name;
        CheckRadios(CvvcPresetFlyout, preset);
    }

    static void CheckRadios(MenuFlyout flyout, object selected)
    {
        foreach (var entry in flyout.Items)
        {
            if (entry is RadioMenuFlyoutItem radio)
                radio.IsChecked = ReferenceEquals(radio.Tag, selected);
        }
    }

    void ScheduleSession()
    {
        if (!_configReady || _applyingSave || _sessionLocked || _sessionTimer == null || _folder == null)
            return;
        _sessionDirty = true;
        _sessionTimer.Stop();
        _sessionTimer.Start();
    }

    void FlushSession(bool force = false)
    {
        _sessionTimer?.Stop();
        if (!_configReady || _applyingSave || _sessionLocked || _folder == null)
            return;
        if (!force && !_sessionDirty)
            return;
        try
        {
            OtoSaveStore.WriteSession(_folder, CaptureSettings(includeLibraries: false));
            _sessionDirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    TextBlock DialogErrorLine()
    {
        var error = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        if (Application.Current.Resources.TryGetValue("SystemFillColorCriticalBrush", out var brush) && brush is Brush paint)
            error.Foreground = paint;
        return error;
    }

    static TextBlock HintText(string text) =>
        new()
        {
            Text = text,
            FontSize = 12,
            Opacity = 0.7,
        };

    static TextBlock PathText(string path) =>
        new()
        {
            Text = path,
            FontSize = 12,
            Opacity = 0.75,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
        };
}
