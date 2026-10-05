namespace Otoasobi;

// 選んだフォルダの original に、直下の WAV の控えを置く。すでにあるファイルは上書きしない。
// フォルダ読込み時は、直下と original の WAV を 44100Hz・16bit・モノラルへ揃える。
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

    public static void Prepare(string folder, Action<string, int, int>? progress)
    {
        Ensure(folder);
        var jobs = new List<(string Path, string Label)>();
        foreach (var name in WavNames(folder))
            jobs.Add((Path.Combine(folder, name), name));
        var original = Path.Combine(folder, FolderName);
        if (Directory.Exists(original))
        {
            foreach (var name in WavNames(original))
                jobs.Add((Path.Combine(original, name), "original/" + name));
        }

        for (var i = 0; i < jobs.Count; i++)
        {
            var (path, label) = jobs[i];
            progress?.Invoke(label, i, jobs.Count);
            try
            {
                WavStandard.Convert(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                throw new IOException($"{label} を変換できませんでした。{ex.Message}", ex);
            }
        }

        if (jobs.Count > 0)
            progress?.Invoke(jobs[^1].Label, jobs.Count, jobs.Count);
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
                    chain.Render(audio.Samples, audio.Channels, audio.SampleRate);
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
