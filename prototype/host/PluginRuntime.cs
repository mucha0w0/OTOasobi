using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Otoasobi;

namespace Otoasobi.Probe;

sealed record PluginCall(string Id, string Request, string Op, string Path, string Text, string Encoding, JsonElement? Body);

sealed record PluginReply(bool Ok, object? Result, string? Error)
{
    public static PluginReply Success(object? result) => new(true, result, null);

    public static PluginReply Fail(string error) => new(false, null, error);
}

// ページの文字列はシェルに渡さない。読む場所は開いている音源フォルダの中だけ。
sealed class PluginRuntime : IDisposable
{
    readonly object _gate = new();
    readonly Dictionary<string, PluginScript> _scripts = new(StringComparer.Ordinal);
    string? _python;
    bool _pythonLooked;
    int _disposed;

    public async Task<PluginReply> Execute(PluginCatalog catalog, string? project, PluginCall call)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return PluginReply.Fail("パネルがありません。");
        var item = catalog.Find(call.Id);
        if (item == null)
            return PluginReply.Fail("パネルがありません。");
        try
        {
            return call.Op switch
            {
                "list" => List(project, call.Path),
                "read" => Read(project, call.Path, call.Encoding),
                "write" => Write(project, call.Path, call.Text, call.Encoding),
                "run" => await Run(item, project, call.Body).ConfigureAwait(false),
                _ => PluginReply.Fail("操作できません。"),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PluginReply.Fail("この場所は読めません。");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;
        lock (_gate)
        {
            foreach (var script in _scripts.Values)
                script.Dispose();
            _scripts.Clear();
        }
    }

    async Task<PluginReply> Run(PluginCatalog.Item item, string? project, JsonElement? body)
    {
        if (string.IsNullOrEmpty(project))
            return PluginReply.Fail("フォルダを開いていません。");
        if (string.IsNullOrEmpty(item.Script) || !File.Exists(item.Script))
            return PluginReply.Fail("スクリプトがありません。");
        var python = Python();
        if (python == null)
            return PluginReply.Fail("解析用の Python が見つかりません。");
        PluginScript script;
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) == 1)
                return PluginReply.Fail("パネルがありません。");
            if (!_scripts.TryGetValue(item.Id, out var found))
            {
                found = new PluginScript();
                _scripts.Add(item.Id, found);
            }

            script = found;
        }

        return await script.Run(python, item.Script, item.Directory, project, body).ConfigureAwait(false);
    }

    string? Python()
    {
        lock (_gate)
        {
            if (_pythonLooked)
                return _python;
            _pythonLooked = true;
            _python = FindPython();
            return _python;
        }
    }

    // 高精度エンジン、同梱の音響用 Python、開発用 .venv の順。解析の起動と同じ。
    static string? FindPython()
    {
        if (File.Exists(AppPaths.EnginePython))
            return AppPaths.EnginePython;
        var root = FindScriptRoot();
        if (root == null)
            return null;
        var slim = Path.Combine(root, "python", "python.exe");
        if (File.Exists(slim))
            return slim;
        var venv = Path.Combine(root, ".venv", "Scripts", "python.exe");
        return File.Exists(venv) ? venv : null;
    }

    static string? FindScriptRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "otoasobi", "worker.py")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }

    static PluginReply List(string? project, string path)
    {
        if (string.IsNullOrEmpty(project))
            return PluginReply.Fail("フォルダを開いていません。");
        var full = PluginPath.Resolve(project, path, allowRoot: true);
        if (full == null)
            return PluginReply.Fail("この場所は読めません。");
        if (!Directory.Exists(full))
            return PluginReply.Fail("フォルダがありません。");
        var names = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(full))
        {
            var name = Path.GetFileName(entry);
            if (!string.IsNullOrEmpty(name))
                names.Add(name);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return PluginReply.Success(new { names });
    }

    static PluginReply Read(string? project, string path, string encodingName)
    {
        if (string.IsNullOrEmpty(project))
            return PluginReply.Fail("フォルダを開いていません。");
        var encoding = TextEncoding(encodingName);
        if (encoding == null)
            return PluginReply.Fail("文字コードを読めません。");
        var full = PluginPath.Resolve(project, path, allowRoot: false);
        if (full == null || Directory.Exists(full))
            return PluginReply.Fail("この場所は読めません。");
        if (!File.Exists(full))
            return PluginReply.Fail("ファイルがありません。");
        if (new FileInfo(full).Length > PluginPath.MaxBytes)
            return PluginReply.Fail("ファイルが大きすぎます。");
        return PluginReply.Success(new { text = encoding.GetString(File.ReadAllBytes(full)) });
    }

    static PluginReply Write(string? project, string path, string text, string encodingName)
    {
        if (string.IsNullOrEmpty(project))
            return PluginReply.Fail("フォルダを開いていません。");
        var encoding = TextEncoding(encodingName);
        if (encoding == null)
            return PluginReply.Fail("文字コードを読めません。");
        var full = PluginPath.Resolve(project, path, allowRoot: false);
        if (full == null || Directory.Exists(full))
            return PluginReply.Fail("この場所は読めません。");
        var bytes = encoding.GetBytes(text ?? "");
        if (bytes.Length > PluginPath.MaxBytes)
            return PluginReply.Fail("ファイルが大きすぎます。");
        var parent = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(parent))
            return PluginReply.Fail("この場所は読めません。");
        Directory.CreateDirectory(parent);
        File.WriteAllBytes(full, bytes);
        return PluginReply.Success(null);
    }

    static Encoding? TextEncoding(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == "utf-8")
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        if (name != "cp932")
            return null;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }
}

sealed class PluginScript : IDisposable
{
    readonly SemaphoreSlim _gate = new(1, 1);
    Process? _process;
    StreamWriter? _input;
    StreamReader? _output;
    int _stopped;

    public async Task<PluginReply> Run(string python, string script, string directory, string project, JsonElement? body)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _stopped) == 1)
                return PluginReply.Fail("スクリプトが終了しました。");
            var line = RequestLine(project, body);
            if (Encoding.UTF8.GetByteCount(line) > PluginPath.MaxBytes)
                return PluginReply.Fail("要求が大きすぎます。");
            try
            {
                Ensure(python, script, directory);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            {
                KillProcess();
                return PluginReply.Fail("スクリプトを起動できませんでした。");
            }

            if (_input == null || _output == null)
                return PluginReply.Fail("スクリプトを起動できませんでした。");
            try
            {
                await _input.WriteLineAsync(line).ConfigureAwait(false);
                await _input.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                KillProcess();
                return PluginReply.Fail("スクリプトが終了しました。");
            }

            var read = _output.ReadLineAsync();
            var finished = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
            if (finished != read)
            {
                KillProcess();
                Observe(read);
                return PluginReply.Fail("スクリプトが時間内に終わりませんでした。");
            }

            string? output;
            try
            {
                output = await read.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                KillProcess();
                return PluginReply.Fail("スクリプトが終了しました。");
            }

            if (output == null)
            {
                KillProcess();
                return PluginReply.Fail("スクリプトが終了しました。");
            }

            try
            {
                using var parsed = JsonDocument.Parse(output);
                return PluginReply.Success(parsed.RootElement.Clone());
            }
            catch (JsonException)
            {
                return PluginReply.Fail("スクリプトの応答を読めませんでした。");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        Volatile.Write(ref _stopped, 1);
        KillProcess();
    }

    static string RequestLine(string project, JsonElement? body)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("project", project);
            writer.WritePropertyName("body");
            if (body.HasValue)
                body.Value.WriteTo(writer);
            else
                writer.WriteNullValue();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    void Ensure(string python, string script, string directory)
    {
        if (_process is { HasExited: false })
            return;
        KillProcess();
        var start = new ProcessStartInfo
        {
            FileName = python,
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        start.ArgumentList.Add("-X");
        start.ArgumentList.Add("utf8");
        start.ArgumentList.Add(script);
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start())
            throw new InvalidOperationException();
        process.BeginErrorReadLine();
        _process = process;
        _input = process.StandardInput;
        _output = process.StandardOutput;
    }

    void KillProcess()
    {
        var process = _process;
        _process = null;
        _input = null;
        _output = null;
        if (process == null)
            return;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        process.Dispose();
    }

    static void Observe(Task task)
    {
        _ = task.ContinueWith(
            done => { _ = done.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }
}
