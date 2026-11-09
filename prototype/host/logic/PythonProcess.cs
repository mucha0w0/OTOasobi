using System.Diagnostics;
using System.Text;

namespace Otoasobi;

// プレビューと解析で同じ Python の探し方と環境にする。
static class PythonProcess
{
    public static Process Start(string module)
    {
        var (repo, python, bundled) = Locate();
        var psi = new ProcessStartInfo
        {
            FileName = python,
            Arguments = "-X utf8 -m " + module,
            WorkingDirectory = repo,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONPATH"] = repo;
        psi.Environment["TORCH_HOME"] = ResolveTorchHome();
        // ダウンロード表示が改行せず、標準エラーが詰まると処理が戻ってこない。
        psi.Environment["TQDM_DISABLE"] = "1";
        if (bundled)
            psi.Environment["PYTHONNOUSERSITE"] = "1";

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!process.Start())
            throw new IOException("解析プロセスを起動できませんでした。");
        return process;
    }

    // アプリ用の model.pt が無いときは、既にある torch のキャッシュを使う。
    // 空の保存先のままだと、音素対応の途中で 1.2GB を取り直して止まる。
    public static string ResolveTorchHome()
    {
        if (File.Exists(AppPaths.ModelFile))
            return AppPaths.ModelsRoot;

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var cached = Path.Combine(profile, ".cache", "torch", "hub", "checkpoints", "model.pt");
        if (File.Exists(cached))
            return Path.Combine(profile, ".cache", "torch");

        return AppPaths.ModelsRoot;
    }

    // 高精度エンジン、同梱の音響用 Python、開発用 .venv の順。スクリプトはアプリ側を使う。
    public static (string Repo, string Python, bool Bundled) Locate()
    {
        var root = FindScriptRoot();
        if (root == null)
            throw new FileNotFoundException("解析用の Python が見つかりません。");

        if (File.Exists(AppPaths.EnginePython))
            return (root, AppPaths.EnginePython, true);

        var slim = Path.Combine(root, "python", "python.exe");
        if (File.Exists(slim))
            return (root, slim, true);

        var venv = Path.Combine(root, ".venv", "Scripts", "python.exe");
        if (File.Exists(venv))
            return (root, venv, false);

        throw new FileNotFoundException("解析用の Python が見つかりません。");
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
}
