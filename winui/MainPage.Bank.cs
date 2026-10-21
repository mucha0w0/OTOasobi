using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace Otoasobi;

public sealed partial class MainPage
{
    readonly List<(string File, string? Kind)> _bankVotes = [];
    readonly List<ProfileRow> _profileRows = [];
    readonly List<LocaleRow> _localeRows = [];
    bool _bankApplying;
    bool _bankPreserveLocales;
    int _iconGen;
    int _portraitGen;
    InMemoryRandomAccessStream? _bankIconStream;
    InMemoryRandomAccessStream? _bankPortraitStream;

    void LoadBank()
    {
        if (string.IsNullOrEmpty(_folder))
            return;
        var folder = _folder;
        try
        {
            var character = new CharacterFile();
            var characterPath = Path.Combine(folder, "character.txt");
            if (File.Exists(characterPath))
                character = VoicebankText.ParseCharacter(VoicebankText.Decode(File.ReadAllBytes(characterPath)));
            var yaml = new VoicebankYaml();
            var yamlPath = Path.Combine(folder, "character.yaml");
            if (File.Exists(yamlPath))
            {
                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
                yaml = VoicebankText.ReadYaml(utf8.GetString(File.ReadAllBytes(yamlPath)));
            }

            var readme = "";
            var readmePath = Path.Combine(folder, "readme.txt");
            if (File.Exists(readmePath))
                readme = VoicebankText.ReadReadme(File.ReadAllBytes(readmePath));
            _bankVotes.Clear();
            _bankVotes.AddRange(VoicebankText.CollectVotes(folder));
            _bankPreserveLocales = yaml.PreserveLocalizedBlock;
            ApplyBank(folder, character, yaml, readme);
        }
        catch (Exception ex)
        {
            ShowNotice("音源設定を読めませんでした", ex.Message);
        }
    }

    void ApplyBank(string folder, CharacterFile character, VoicebankYaml yaml, string readme)
    {
        _bankApplying = true;
        try
        {
            BankNameBox.Text = Shared(character.HasName, character.Name, yaml.HasName, yaml.Name);
            BankYamlNameBox.Text = yaml.HasName ? yaml.Name : BankNameBox.Text;
            var image = Shared(character.HasImage, character.Image, yaml.HasImage, yaml.Image);
            var yamlImage = yaml.HasImage ? yaml.Image : image;
            if (!character.HasImage && !yaml.HasImage && File.Exists(Path.Combine(folder, "icon.bmp")))
            {
                image = "icon.bmp";
                yamlImage = "icon.bmp";
            }

            BankImageBox.Text = image;
            BankYamlImageBox.Text = yamlImage;
            BankAuthorBox.Text = Shared(character.HasAuthor, character.Author, yaml.HasAuthor, yaml.Author);
            BankYamlAuthorBox.Text = yaml.HasAuthor ? yaml.Author : BankAuthorBox.Text;
            BankWebBox.Text = Shared(character.HasWeb, character.Web, yaml.HasWeb, yaml.Web);
            BankYamlWebBox.Text = yaml.HasWeb ? yaml.Web : BankWebBox.Text;
            BankVoiceBox.Text = yaml.HasVoice ? yaml.Voice : "";
            BankVersionBox.Text = yaml.HasVersion ? yaml.Version : "";
            FillSamples(character.HasSample ? character.Sample : "");
            BankProfileHost.Children.Clear();
            _profileRows.Clear();
            foreach (var line in character.Profile)
                AddProfileRow(line.Label, line.Value, line.Fullwidth);
            BankExtraBox.Text = string.Join('\n', character.Extra);
            BankReadmeBox.Text = readme;
            BankLocaleHost.Children.Clear();
            _localeRows.Clear();
            foreach (var (code, name) in yaml.LocalizedNames)
                AddLocaleRow(code, name);
            BankPortraitPathBox.Text = yaml.HasPortrait ? VoicebankText.NormalizeRelative(yaml.Portrait) : "";
            BankOpacitySlider.Value = yaml.HasOpacity ? yaml.PortraitOpacity : 0.67;
            BankOpacityValue.Text = VoicebankText.FormatOpacity(BankOpacitySlider.Value);
            BankPortraitHeightBox.Value = yaml.HasHeight ? yaml.PortraitHeight : 0;
        }
        finally
        {
            _bankApplying = false;
        }

        RefreshBankEncoding();
        _iconGen++;
        _portraitGen++;
        _ = ShowIconAsync(folder, _iconGen);
        _ = ShowPortraitAsync(folder, _portraitGen);
    }

    static string Shared(bool hasCharacter, string character, bool hasYaml, string yaml)
    {
        if (hasCharacter)
            return character;
        return hasYaml ? yaml : "";
    }

    void FillSamples(string selected)
    {
        var names = new List<string> { VoicebankText.RandomSample };
        if (!string.IsNullOrEmpty(_folder) && Directory.Exists(_folder))
        {
            foreach (var path in Directory.EnumerateFiles(_folder))
            {
                if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    names.Add(Path.GetFileName(path));
            }

            if (names.Count > 1)
                names.Sort(1, names.Count - 1, StringComparer.OrdinalIgnoreCase);
        }

        if (selected.Length > 0 && !names.Any(name => name.Equals(selected, StringComparison.OrdinalIgnoreCase)))
            names.Add(selected);
        BankSampleBox.ItemsSource = names;
        var wanted = selected.Length == 0 ? VoicebankText.RandomSample : selected;
        BankSampleBox.SelectedItem = names.FirstOrDefault(name => name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            ?? VoicebankText.RandomSample;
    }

    void BankShared_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_bankApplying || sender is not TextBox source || source.Tag is not string key)
            return;
        var other = key switch
        {
            "name" => source == BankNameBox ? BankYamlNameBox : BankNameBox,
            "image" => source == BankImageBox ? BankYamlImageBox : BankImageBox,
            "author" => source == BankAuthorBox ? BankYamlAuthorBox : BankAuthorBox,
            "web" => source == BankWebBox ? BankYamlWebBox : BankWebBox,
            _ => null,
        };
        if (other != null && other.Text != source.Text)
        {
            _bankApplying = true;
            other.Text = source.Text;
            _bankApplying = false;
        }

        RefreshBankEncoding();
    }

    void BankSample_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_bankApplying)
            RefreshBankEncoding();
    }

    void BankForm_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_bankApplying)
            RefreshBankEncoding();
    }

    void BankPortraitPath_Changed(object sender, TextChangedEventArgs e)
    {
        if (_bankApplying || string.IsNullOrEmpty(_folder))
            return;
        _portraitGen++;
        _ = ShowPortraitAsync(_folder, _portraitGen);
    }

    void BankOpacity_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (BankOpacityValue == null)
            return;
        BankOpacityValue.Text = VoicebankText.FormatOpacity(e.NewValue);
    }

    void BankAddProfile_Click(object sender, RoutedEventArgs e) => AddProfileRow("", "", fullwidth: true);

    void BankAddLocale_Click(object sender, RoutedEventArgs e) => AddLocaleRow("", "");

    void BankPickIcon_Click(object sender, RoutedEventArgs e) => _ = PickIconAsync();

    void BankPickPortrait_Click(object sender, RoutedEventArgs e) => _ = PickPortraitAsync();

    void BankSave_Click(object sender, RoutedEventArgs e) => SaveBank();

    void AddProfileRow(string label, string value, bool fullwidth)
    {
        var row = new ProfileRow(label, value, fullwidth);
        row.Remove.Style = (Style)Resources["ToolbarButtonStyle"];
        row.Label.TextChanged += BankForm_Changed;
        row.Value.TextChanged += BankForm_Changed;
        row.Remove.Click += (_, _) =>
        {
            _profileRows.Remove(row);
            BankProfileHost.Children.Remove(row.Root);
            RefreshBankEncoding();
        };
        BankProfileHost.Children.Add(row.Root);
        _profileRows.Add(row);
    }

    void AddLocaleRow(string code, string name)
    {
        var row = new LocaleRow(code, name);
        row.Remove.Style = (Style)Resources["ToolbarButtonStyle"];
        row.Remove.Click += (_, _) =>
        {
            _localeRows.Remove(row);
            BankLocaleHost.Children.Remove(row.Root);
        };
        BankLocaleHost.Children.Add(row.Root);
        _localeRows.Add(row);
    }

    void RefreshBankEncoding()
    {
        if (_bankApplying || BankEncodingText == null)
            return;
        var encoding = VoicebankText.Resolve(_bankVotes, VoicebankText.FormatCharacter(CollectCharacter()));
        BankEncodingText.Text = "text_file_encoding: " + encoding;
    }

    CharacterFile CollectCharacter()
    {
        var file = new CharacterFile
        {
            Name = BankNameBox.Text,
            Image = BankImageBox.Text,
            Sample = SampleFromUi(),
            Author = BankAuthorBox.Text,
            Web = BankWebBox.Text,
        };
        foreach (var row in _profileRows)
        {
            if (row.Label.Text.Trim().Length == 0 && row.Value.Text.Trim().Length == 0)
                continue;
            file.Profile.Add(new ProfileLine(row.Label.Text, row.Value.Text, row.Fullwidth));
        }

        var extra = (BankExtraBox.Text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (var line in extra.Split('\n'))
        {
            if (line.Trim().Length == 0)
                continue;
            file.Extra.Add(line.TrimEnd());
        }

        return file;
    }

    string SampleFromUi()
    {
        if (BankSampleBox.SelectedItem is not string name || name == VoicebankText.RandomSample)
            return "";
        return name;
    }

    List<(string Key, string Value)> CollectLocales(out string? error)
    {
        error = null;
        var list = new List<(string Key, string Value)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _localeRows)
        {
            var code = (row.Language.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            code = code.Trim();
            var name = row.Name.Text.Trim();
            if (code.Length == 0 || name.Length == 0)
                continue;
            foreach (var (known, _) in VoicebankText.Languages)
            {
                if (known.Equals(code, StringComparison.OrdinalIgnoreCase))
                {
                    code = known;
                    break;
                }
            }

            if (!seen.Add(code))
            {
                error = "同じ言語が2つあります。";
                return list;
            }

            list.Add((code, name));
        }

        return list;
    }

    void SaveBank()
    {
        if (_busy || string.IsNullOrEmpty(_folder))
            return;
        var folder = _folder;
        try
        {
            var character = CollectCharacter();
            var body = VoicebankText.FormatCharacter(character);
            var encoding = VoicebankText.Resolve(_bankVotes, body);
            if (!VoicebankText.CanEncode(body, encoding))
            {
                ShowNotice(
                    "音源設定を保存できませんでした",
                    "character.txt に、Shift_JIS で保存できない文字があります。oto.ini か prefix.map が Shift_JIS なので、文字コードは変えられません。");
                return;
            }

            var readme = BankReadmeBox.Text ?? "";
            var readmePath = Path.Combine(folder, "readme.txt");
            var writeReadme = readme.Length > 0 || File.Exists(readmePath);
            if (writeReadme && !VoicebankText.CanEncode(readme, VoicebankText.ShiftJis))
            {
                ShowNotice("音源設定を保存できませんでした", "readme.txt に、ANSI（Shift_JIS）で保存できない文字があります。");
                return;
            }

            var locales = CollectLocales(out var localeError);
            if (localeError != null)
            {
                ShowNotice("音源設定を保存できませんでした", localeError);
                return;
            }

            var portrait = VoicebankText.NormalizeRelative(BankPortraitPathBox.Text);
            if (portrait.Length > 0 && !VoicebankText.TryResolveInside(folder, portrait, out _))
            {
                ShowNotice("音源設定を保存できませんでした", "立ち絵のパスが、音源フォルダの外を指しています。");
                return;
            }

            var yaml = new VoicebankYaml
            {
                TextFileEncoding = encoding,
                Name = BankYamlNameBox.Text.Trim(),
                Image = BankYamlImageBox.Text.Trim(),
                Author = BankYamlAuthorBox.Text.Trim(),
                Voice = BankVoiceBox.Text.Trim(),
                Version = BankVersionBox.Text.Trim(),
                Web = BankYamlWebBox.Text.Trim(),
                Portrait = portrait,
                PortraitOpacity = BankOpacitySlider.Value,
                PortraitHeight = PortraitHeightFromUi(),
                PreserveLocalizedBlock = _bankPreserveLocales && locales.Count == 0,
            };
            foreach (var locale in locales)
                yaml.LocalizedNames.Add(locale);

            var yamlPath = Path.Combine(folder, "character.yaml");
            string? existing = null;
            if (File.Exists(yamlPath))
            {
                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
                existing = utf8.GetString(File.ReadAllBytes(yamlPath));
            }

            WriteBytes(Path.Combine(folder, "character.txt"), VoicebankText.EncodeCharacter(character, encoding));
            if (writeReadme)
                WriteBytes(readmePath, VoicebankText.EncodeReadme(readme));
            var yamlText = VoicebankText.ApplyYaml(existing, yaml);
            WriteBytes(yamlPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(yamlText));
            if (_folder != folder)
                return;
            _bankVotes.Clear();
            _bankVotes.AddRange(VoicebankText.CollectVotes(folder));
            if (locales.Count > 0)
                _bankPreserveLocales = false;
            RefreshBankEncoding();
            BankEncodingText.Text = "text_file_encoding: " + encoding;
            ShowNotice(
                "音源設定を保存しました",
                writeReadme
                    ? "character.txt、readme.txt、character.yaml を書きました。"
                    : "character.txt と character.yaml を書きました。");
        }
        catch (Exception ex)
        {
            if (_folder == folder)
                ShowNotice("音源設定を保存できませんでした", ex.Message);
        }
    }

    int PortraitHeightFromUi()
    {
        if (!double.IsFinite(BankPortraitHeightBox.Value))
            return 0;
        return Math.Max(0, (int)Math.Round(BankPortraitHeightBox.Value));
    }

    async Task PickIconAsync()
    {
        if (_busy || string.IsNullOrEmpty(_folder))
        {
            ShowNotice("アイコンを設定できませんでした", "フォルダを開いてください。");
            return;
        }

        var folder = _folder;
        try
        {
            var source = await PickImageAsync();
            if (source == null || _folder != folder)
                return;
            var (pixels, width, height) = await DecodeImageAsync(source, 512);
            if (_folder != folder)
                return;
            var fitted = VoicebankImage.FitIcon(pixels, width, height);
            var bmp = VoicebankImage.EncodeBmp24(fitted, VoicebankImage.IconSize, VoicebankImage.IconSize);
            WriteBytes(Path.Combine(folder, "icon.bmp"), bmp);
            _bankApplying = true;
            BankImageBox.Text = "icon.bmp";
            BankYamlImageBox.Text = "icon.bmp";
            _bankApplying = false;
            RefreshBankEncoding();
            _iconGen++;
            await ShowIconAsync(folder, _iconGen);
            ShowNotice("アイコンを設定しました", "100×100 の icon.bmp にしました。文字の設定は「音源設定を保存」で書き込みます。");
        }
        catch (Exception ex)
        {
            if (_folder == folder)
                ShowNotice("アイコンを設定できませんでした", ex.Message);
        }
    }

    async Task PickPortraitAsync()
    {
        if (_busy || string.IsNullOrEmpty(_folder))
        {
            ShowNotice("立ち絵を設定できませんでした", "フォルダを開いてください。");
            return;
        }

        var folder = _folder;
        try
        {
            var source = await PickImageAsync();
            if (source == null || _folder != folder)
                return;
            var relative = VoicebankText.PortraitFileName(BankPortraitPathBox.Text);
            if (!VoicebankText.TryResolveInside(folder, relative, out var full))
            {
                ShowNotice("立ち絵を設定できませんでした", "音源フォルダの中の相対パスにしてください。");
                return;
            }

            var (pixels, width, height) = await DecodeImageAsync(source, 4096);
            if (_folder != folder)
                return;
            var encoded = await EncodePortraitAsync(pixels, width, height, relative);
            WriteBytes(full, encoded);
            BankPortraitPathBox.Text = relative;
            _portraitGen++;
            await ShowPortraitAsync(folder, _portraitGen);
            ShowNotice("立ち絵を設定しました", "パスは「音源設定を保存」で character.yaml に書き込みます。");
        }
        catch (Exception ex)
        {
            if (_folder == folder)
                ShowNotice("立ち絵を設定できませんでした", ex.Message);
        }
    }

    async Task<string?> PickImageAsync()
    {
        if (App.MainWindow == null)
            return null;
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".ico" })
            picker.FileTypeFilter.Add(ext);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    async Task ShowIconAsync(string folder, int generation)
    {
        var path = Path.Combine(folder, "icon.bmp");
        await ShowFilePreviewAsync(path, BankIconImage, BankIconMissing, generation, () => _iconGen, stream =>
        {
            _bankIconStream?.Dispose();
            _bankIconStream = stream;
        });
    }

    async Task ShowPortraitAsync(string folder, int generation)
    {
        var relative = VoicebankText.NormalizeRelative(BankPortraitPathBox.Text);
        var path = "";
        if (relative.Length > 0 && VoicebankText.TryResolveInside(folder, relative, out var full))
            path = full;
        await ShowFilePreviewAsync(path, BankPortraitImage, BankPortraitMissing, generation, () => _portraitGen, stream =>
        {
            _bankPortraitStream?.Dispose();
            _bankPortraitStream = stream;
        });
    }

    async Task ShowFilePreviewAsync(
        string path,
        Image image,
        TextBlock missing,
        int generation,
        Func<int> current,
        Action<InMemoryRandomAccessStream> keep)
    {
        if (path.Length == 0 || !File.Exists(path))
        {
            if (generation != current())
                return;
            image.Source = null;
            image.Visibility = Visibility.Collapsed;
            missing.Visibility = Visibility.Visible;
            return;
        }

        LoadedBitmap? loaded = null;
        try
        {
            loaded = await LoadBitmapAsync(path);
            if (loaded == null || generation != current())
            {
                loaded?.Stream.Dispose();
                return;
            }

            image.Source = loaded.Image;
            image.Visibility = Visibility.Visible;
            missing.Visibility = Visibility.Collapsed;
            keep(loaded.Stream);
        }
        catch (Exception)
        {
            loaded?.Stream.Dispose();
            if (generation != current())
                return;
            image.Source = null;
            image.Visibility = Visibility.Collapsed;
            missing.Visibility = Visibility.Visible;
        }
    }

    static async Task<LoadedBitmap?> LoadBitmapAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        var stream = new InMemoryRandomAccessStream();
        try
        {
            var writer = new DataWriter(stream);
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            writer.DetachStream();
            writer.Dispose();
            stream.Seek(0);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            return new LoadedBitmap(image, stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    static async Task<(byte[] Pixels, int Width, int Height)> DecodeImageAsync(string path, uint maxEdge)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        using var stream = new InMemoryRandomAccessStream();
        var writer = new DataWriter(stream);
        writer.WriteBytes(bytes);
        await writer.StoreAsync();
        writer.DetachStream();
        writer.Dispose();
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var width = decoder.OrientedPixelWidth;
        var height = decoder.OrientedPixelHeight;
        if (width == 0 || height == 0)
            throw new InvalidDataException("画像の大きさを読めませんでした。");
        var transform = new BitmapTransform { InterpolationMode = BitmapInterpolationMode.Fant };
        if (width > maxEdge || height > maxEdge)
        {
            var scale = Math.Min(maxEdge / (double)width, maxEdge / (double)height);
            width = (uint)Math.Max(1, Math.Round(width * scale));
            height = (uint)Math.Max(1, Math.Round(height * scale));
            transform.ScaledWidth = width;
            transform.ScaledHeight = height;
        }

        var data = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);
        var pixels = data.DetachPixelData();
        if (pixels.Length < (long)width * height * 4)
            throw new InvalidDataException("画像の画素を読めませんでした。");
        return (pixels, (int)width, (int)height);
    }

    static async Task<byte[]> EncodePortraitAsync(byte[] pixels, int width, int height, string relative)
    {
        var ext = Path.GetExtension(relative);
        if (ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            return VoicebankImage.EncodeBmp24(VoicebankImage.FlattenOnWhite(pixels), width, height);
        var png = !ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            && !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
        var source = png ? pixels : VoicebankImage.FlattenOnWhite(pixels);
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(png ? BitmapEncoder.PngEncoderId : BitmapEncoder.JpegEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            png ? BitmapAlphaMode.Straight : BitmapAlphaMode.Ignore,
            (uint)width,
            (uint)height,
            96,
            96,
            source);
        await encoder.FlushAsync();
        stream.Seek(0);
        var size = (uint)stream.Size;
        var output = new byte[size];
        var reader = new DataReader(stream);
        await reader.LoadAsync(size);
        reader.ReadBytes(output);
        reader.Dispose();
        return output;
    }

    static void WriteBytes(string path, byte[] bytes)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var temp = path + ".writing";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            throw;
        }
    }

    sealed record LoadedBitmap(BitmapImage Image, InMemoryRandomAccessStream Stream);

    sealed class ProfileRow
    {
        public ProfileRow(string label, string value, bool fullwidth)
        {
            Fullwidth = fullwidth;
            Label = new TextBox
            {
                Text = label,
                MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                PlaceholderText = "項目",
            };
            Value = new TextBox
            {
                Text = value,
                MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                PlaceholderText = "内容",
            };
            AutomationProperties.SetName(Label, "プロフィールの項目");
            AutomationProperties.SetName(Value, "プロフィールの内容");
            var mark = new TextBlock
            {
                Text = fullwidth ? "：" : ":",
                VerticalAlignment = VerticalAlignment.Center,
            };
            Remove = new Button
            {
                Content = "削除",
                MinWidth = 0,
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(Remove, "プロフィールの行を削除");
            Root = new Grid { ColumnSpacing = 8 };
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(Value, 2);
            Grid.SetColumn(Remove, 3);
            Grid.SetColumn(mark, 1);
            Root.Children.Add(Label);
            Root.Children.Add(mark);
            Root.Children.Add(Value);
            Root.Children.Add(Remove);
        }

        public Grid Root { get; }

        public TextBox Label { get; }

        public TextBox Value { get; }

        public Button Remove { get; }

        public bool Fullwidth { get; }
    }

    sealed class LocaleRow
    {
        public LocaleRow(string code, string name)
        {
            Language = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 0,
                PlaceholderText = "言語",
            };
            AutomationProperties.SetName(Language, "ローカライズの言語");
            foreach (var (lang, label) in VoicebankText.Languages)
                Language.Items.Add(new ComboBoxItem { Content = $"{lang}　{label}", Tag = lang });
            ComboBoxItem? selected = null;
            var wanted = code.Trim();
            foreach (var item in Language.Items)
            {
                if (item is ComboBoxItem choice && choice.Tag is string tag && tag.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    selected = choice;
                    break;
                }
            }

            if (selected == null && wanted.Length > 0)
            {
                selected = new ComboBoxItem { Content = wanted, Tag = wanted };
                Language.Items.Add(selected);
            }

            Language.SelectedItem = selected;
            Name = new TextBox
            {
                Text = name,
                MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                PlaceholderText = "その言語での名前",
            };
            AutomationProperties.SetName(Name, "ローカライズした名前");
            Remove = new Button
            {
                Content = "削除",
                MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            AutomationProperties.SetName(Remove, "ローカライズの行を削除");
            var line = new Grid { ColumnSpacing = 8 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(Remove, 1);
            line.Children.Add(Name);
            line.Children.Add(Remove);
            Root = new StackPanel { Spacing = 4 };
            Root.Children.Add(Language);
            Root.Children.Add(line);
        }

        public StackPanel Root { get; }

        public ComboBox Language { get; }

        public TextBox Name { get; }

        public Button Remove { get; }
    }
}
