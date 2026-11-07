using System.Text;
using System.Text.Json;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Otoasobi.Probe;

sealed class ProbeBank
{
    string? _folder;
    int _generation;
    bool _preserveLocales;
    readonly List<(string File, string? Kind)> _votes = [];

    public int Generation => _generation;

    public void Open(string folder)
    {
        _folder = folder;
        _generation++;
        _preserveLocales = false;
        _votes.Clear();
        _votes.AddRange(VoicebankText.CollectVotes(folder));
        var yamlPath = Path.Combine(folder, "character.yaml");
        if (!File.Exists(yamlPath))
            return;
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var yaml = VoicebankText.ReadYaml(utf8.GetString(File.ReadAllBytes(yamlPath)));
            _preserveLocales = yaml.PreserveLocalizedBlock;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
        }
    }

    public string? Save(JsonElement body, int generation)
    {
        if (string.IsNullOrEmpty(_folder) || generation != _generation)
            return null;
        try
        {
            var character = ReadCharacter(body);
            var bodyText = VoicebankText.FormatCharacter(character);
            var encoding = VoicebankText.Resolve(_votes, bodyText);
            if (!VoicebankText.CanEncode(bodyText, encoding))
                return "character.txt に、Shift_JIS で保存できない文字があります。oto.ini か prefix.map が Shift_JIS なので、文字コードは変えられません。";
            var readme = Text(body, "readme");
            var readmePath = Path.Combine(_folder, "readme.txt");
            var writeReadme = readme.Length > 0 || File.Exists(readmePath);
            if (writeReadme && !VoicebankText.CanEncode(readme, VoicebankText.ShiftJis))
                return "readme.txt に、ANSI（Shift_JIS）で保存できない文字があります。";
            var locales = ReadLocales(body, out var localeError);
            if (localeError != null)
                return localeError;
            var portrait = VoicebankText.NormalizeRelative(Text(body, "portrait"));
            if (portrait.Length > 0 && !VoicebankText.TryResolveInside(_folder, portrait, out _))
                return "立ち絵のパスが、音源フォルダの外を指しています。";

            var yaml = new VoicebankYaml
            {
                TextFileEncoding = encoding,
                Name = Text(body, "name").Trim(),
                Image = Text(body, "image").Trim(),
                Author = Text(body, "author").Trim(),
                Voice = Text(body, "voice").Trim(),
                Version = Text(body, "version").Trim(),
                Web = Text(body, "web").Trim(),
                Portrait = portrait,
                PortraitOpacity = Clamp(Number(body, "opacity"), 0, 1, 0.67),
                PortraitHeight = Math.Max(0, (int)Math.Round(Number(body, "height"))),
                PreserveLocalizedBlock = _preserveLocales && locales.Count == 0,
            };
            foreach (var locale in locales)
                yaml.LocalizedNames.Add(locale);
            string? existing = null;
            var yamlPath = Path.Combine(_folder, "character.yaml");
            if (File.Exists(yamlPath))
            {
                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
                existing = utf8.GetString(File.ReadAllBytes(yamlPath));
            }

            Write(Path.Combine(_folder, "character.txt"), VoicebankText.EncodeCharacter(character, encoding));
            if (writeReadme)
                Write(readmePath, VoicebankText.EncodeReadme(readme));
            var yamlText = VoicebankText.ApplyYaml(existing, yaml);
            Write(yamlPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(yamlText));
            _preserveLocales = yaml.PreserveLocalizedBlock;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EncoderFallbackException or DecoderFallbackException or ArgumentException)
        {
            return ex.Message;
        }
    }

    public async Task<string?> SaveIconAsync(string source)
    {
        if (string.IsNullOrEmpty(_folder))
            return "フォルダを開いてください。";
        var (pixels, width, height) = await DecodeAsync(source, 512);
        var fitted = VoicebankImage.FitIcon(pixels, width, height);
        var bmp = VoicebankImage.EncodeBmp24(fitted, VoicebankImage.IconSize, VoicebankImage.IconSize);
        Write(Path.Combine(_folder, "icon.bmp"), bmp);
        return Stamp(image: "icon.bmp", portrait: null);
    }

    public async Task<string?> SavePortraitAsync(string source)
    {
        if (string.IsNullOrEmpty(_folder))
            return "フォルダを開いてください。";
        var relative = VoicebankText.PortraitFileName(CurrentPortrait());
        if (!VoicebankText.TryResolveInside(_folder, relative, out var full))
            return "音源フォルダの中の相対パスにしてください。";
        var (pixels, width, height) = await DecodeAsync(source, 4096);
        var encoded = await EncodePortraitAsync(pixels, width, height, relative);
        Write(full, encoded);
        return Stamp(image: null, portrait: relative);
    }

    string? Stamp(string? image, string? portrait)
    {
        if (string.IsNullOrEmpty(_folder))
            return "フォルダを開いてください。";
        var folder = _folder;
        var character = new CharacterFile();
        var characterPath = Path.Combine(folder, "character.txt");
        if (File.Exists(characterPath))
            character = VoicebankText.ParseCharacter(VoicebankText.Decode(File.ReadAllBytes(characterPath)));
        if (image != null)
            character.Image = image;
        var body = VoicebankText.FormatCharacter(character);
        var encoding = VoicebankText.Resolve(_votes, body);
        if (!VoicebankText.CanEncode(body, encoding))
            return "character.txt に、Shift_JIS で保存できない文字があります。oto.ini か prefix.map が Shift_JIS なので、文字コードは変えられません。";
        var yaml = new VoicebankYaml();
        string? existing = null;
        var yamlPath = Path.Combine(folder, "character.yaml");
        if (File.Exists(yamlPath))
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            existing = utf8.GetString(File.ReadAllBytes(yamlPath));
            yaml = VoicebankText.ReadYaml(existing);
        }

        yaml.TextFileEncoding = encoding;
        if (image != null)
            yaml.Image = image;
        if (portrait != null)
            yaml.Portrait = portrait;
        yaml.PreserveLocalizedBlock = _preserveLocales && yaml.LocalizedNames.Count == 0;
        Write(characterPath, VoicebankText.EncodeCharacter(character, encoding));
        Write(yamlPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(VoicebankText.ApplyYaml(existing, yaml)));
        return null;
    }

    string CurrentPortrait()
    {
        var yamlPath = Path.Combine(_folder!, "character.yaml");
        if (!File.Exists(yamlPath))
            return "";
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return VoicebankText.ReadYaml(utf8.GetString(File.ReadAllBytes(yamlPath))).Portrait;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return "";
        }
    }

    public async Task<object> SnapshotAsync()
    {
        var folder = _folder ?? "";
        var character = new CharacterFile();
        var characterPath = Path.Combine(folder, "character.txt");
        if (folder.Length > 0 && File.Exists(characterPath))
            character = VoicebankText.ParseCharacter(VoicebankText.Decode(File.ReadAllBytes(characterPath)));
        var yaml = new VoicebankYaml();
        var yamlPath = Path.Combine(folder, "character.yaml");
        if (folder.Length > 0 && File.Exists(yamlPath))
        {
            try
            {
                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
                yaml = VoicebankText.ReadYaml(utf8.GetString(File.ReadAllBytes(yamlPath)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
            }
        }

        var readme = "";
        var readmePath = Path.Combine(folder, "readme.txt");
        if (folder.Length > 0 && File.Exists(readmePath))
            readme = VoicebankText.ReadReadme(File.ReadAllBytes(readmePath));
        var name = Shared(character.HasName, character.Name, yaml.HasName, yaml.Name);
        var image = Shared(character.HasImage, character.Image, yaml.HasImage, yaml.Image);
        if (!character.HasImage && !yaml.HasImage && folder.Length > 0 && File.Exists(Path.Combine(folder, "icon.bmp")))
            image = "icon.bmp";
        var author = Shared(character.HasAuthor, character.Author, yaml.HasAuthor, yaml.Author);
        var web = Shared(character.HasWeb, character.Web, yaml.HasWeb, yaml.Web);
        var profile = character.Profile.Select(line => new { label = line.Label, value = line.Value, fullwidth = line.Fullwidth }).ToArray();
        if (profile.Length == 0)
            profile = [new { label = "", value = "", fullwidth = true }];
        var locales = yaml.LocalizedNames.Select(item => new { code = item.Key, name = item.Value }).ToArray();
        if (locales.Length == 0)
            locales = [new { code = "", name = "" }];
        var portrait = yaml.HasPortrait ? VoicebankText.NormalizeRelative(yaml.Portrait) : "";
        var encoding = VoicebankText.Resolve(_votes, VoicebankText.FormatCharacter(character));
        string? portraitPath = null;
        if (portrait.Length > 0 && VoicebankText.TryResolveInside(folder, portrait, out var full))
            portraitPath = full;
        return new
        {
            type = "bank",
            generation = _generation,
            name,
            image,
            sample = character.HasSample ? character.Sample : "",
            samples = Samples(folder, character.HasSample ? character.Sample : ""),
            author,
            web,
            profile,
            extra = string.Join('\n', character.Extra),
            readme,
            encoding,
            voice = yaml.HasVoice ? yaml.Voice : "",
            version = yaml.HasVersion ? yaml.Version : "",
            locales,
            languages = VoicebankText.Languages.Select(item => new { code = item.Code, label = item.Label }).ToArray(),
            portrait,
            opacity = yaml.HasOpacity ? yaml.PortraitOpacity : 0.67,
            height = yaml.HasHeight ? yaml.PortraitHeight : 0,
            icon = await PngDataUrlAsync(Path.Combine(folder, "icon.bmp"), 100),
            portraitImage = await PngDataUrlAsync(portraitPath ?? "", 640),
        };
    }

    static CharacterFile ReadCharacter(JsonElement body)
    {
        var sample = Text(body, "sample").Trim();
        var file = new CharacterFile
        {
            Name = Text(body, "name"),
            Image = Text(body, "image"),
            Sample = sample == VoicebankText.RandomSample ? "" : sample,
            Author = Text(body, "author"),
            Web = Text(body, "web"),
        };
        if (body.TryGetProperty("profile", out var profile) && profile.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in profile.EnumerateArray())
            {
                var label = Text(row, "label");
                var value = Text(row, "value");
                if (label.Trim().Length == 0 && value.Trim().Length == 0)
                    continue;
                var fullwidth = !row.TryGetProperty("fullwidth", out var mark) || mark.ValueKind != JsonValueKind.False;
                file.Profile.Add(new ProfileLine(label, value, fullwidth));
            }
        }

        foreach (var line in Text(body, "extra").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Trim().Length == 0)
                continue;
            file.Extra.Add(line.TrimEnd());
        }

        return file;
    }

    static List<(string Key, string Value)> ReadLocales(JsonElement body, out string? error)
    {
        error = null;
        var list = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!body.TryGetProperty("locales", out var rows) || rows.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var row in rows.EnumerateArray())
        {
            var code = Text(row, "code").Trim();
            var name = Text(row, "name").Trim();
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

    static string[] Samples(string folder, string selected)
    {
        var names = new List<string> { VoicebankText.RandomSample };
        if (Directory.Exists(folder))
        {
            foreach (var path in Directory.EnumerateFiles(folder))
            {
                if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    names.Add(Path.GetFileName(path));
            }

            if (names.Count > 1)
                names.Sort(1, names.Count - 1, StringComparer.OrdinalIgnoreCase);
        }

        if (selected.Length > 0 && !names.Any(name => name.Equals(selected, StringComparison.OrdinalIgnoreCase)))
            names.Add(selected);
        return names.ToArray();
    }

    static string Shared(bool characterHas, string character, bool yamlHas, string yaml)
    {
        if (yamlHas)
            return yaml;
        return characterHas ? character : "";
    }

    static async Task<string?> PngDataUrlAsync(string path, uint maxEdge)
    {
        if (path.Length == 0 || !File.Exists(path))
            return null;
        try
        {
            var (pixels, width, height) = await DecodeAsync(path, maxEdge);
            var png = await EncodePortraitAsync(pixels, width, height, "preview.png");
            return "data:image/png;base64," + Convert.ToBase64String(png);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    static async Task<(byte[] Pixels, int Width, int Height)> DecodeAsync(string path, uint maxEdge)
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
        var png = !ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
        var source = png ? pixels : VoicebankImage.FlattenOnWhite(pixels);
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(png ? BitmapEncoder.PngEncoderId : BitmapEncoder.JpegEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, png ? BitmapAlphaMode.Straight : BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, source);
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

    static void Write(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var temp = path + ".writing";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }

    static string Text(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    static double Number(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) && value.TryGetDouble(out var parsed) ? parsed : 0;

    static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
