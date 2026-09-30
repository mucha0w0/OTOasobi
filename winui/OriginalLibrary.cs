namespace Otoasobi;

// 選んだフォルダの original に、直下の WAV の控えを置く。すでにあるファイルは上書きしない。
static class OriginalLibrary
{
    public const string FolderName = "original";

    public static void Ensure(string folder)
    {
        var original = Path.Combine(folder, FolderName);
        Directory.CreateDirectory(original);
        foreach (var name in WavNames(folder))
        {
            var source = Path.Combine(folder, name);
            var dest = Path.Combine(original, name);
            if (SameFile(source, dest) || File.Exists(dest))
                continue;
            File.Copy(source, dest, overwrite: false);
        }
    }

    public static void Apply(string folder, EffectChain chain, Action<string, int, int>? progress)
    {
        Ensure(folder);
        var original = Path.Combine(folder, FolderName);
        var names = WavNames(original);
        if (names.Count == 0)
            throw new InvalidDataException("original に WAV がありません。");
        var staged = new List<(string Temp, string Dest)>();
        try
        {
            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i];
                progress?.Invoke(name, i, names.Count);
                var dest = Path.Combine(folder, name);
                var temp = dest + ".applying";
                try
                {
                    var audio = WavAudio.Read(Path.Combine(original, name));
                    chain.Reset();
                    chain.Process(audio.Samples, audio.Channels, audio.SampleRate);
                    WavAudio.Write(temp, audio);
                    staged.Add((temp, dest));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    throw new IOException($"{name} を適用できませんでした。{ex.Message}", ex);
                }
            }

            foreach (var (temp, dest) in staged)
                File.Move(temp, dest, overwrite: true);
        }
        catch
        {
            foreach (var (temp, _) in staged)
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }

            throw;
        }
    }

    static List<string> WavNames(string folder)
    {
        var names = new List<string>();
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                names.Add(Path.GetFileName(path));
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    static bool SameFile(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
