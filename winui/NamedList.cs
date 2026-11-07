namespace Otoasobi;

interface INamedPreset
{
    string Name { get; }
    bool BuiltIn { get; }
}

// プリセットと配置テンプレートの名前。空、40文字超、予約名、重複を捨てる。
// 制御文字と null 要素を捨てるかは呼び出し側が決める。数値の検査も呼び出し側。
// keep が null を返した名前も使用済みにする。空のエフェクト列や無効な配置は、同じ名前の次の要素を拾わない。
static class NamedList
{
    public const int MaxNameLength = 40;

    public static string DataFile(string fileName) =>
        Path.Combine(AppPaths.DataRoot, fileName);

    public static List<T> Collect<T>(
        IEnumerable<T>? list,
        Func<T, string> readName,
        Func<string, bool>? reserved,
        Func<T, string, T?> keep,
        bool skipNull = false,
        bool rejectControl = false) where T : class
    {
        var seen = new HashSet<string>();
        var result = new List<T>();
        if (list == null)
            return result;
        foreach (var item in list)
        {
            if (skipNull && item == null)
                continue;
            var name = readName(item).Trim();
            if (name.Length == 0 || name.Length > MaxNameLength)
                continue;
            if (rejectControl && name.Any(char.IsControl))
                continue;
            if ((reserved != null && reserved(name)) || !seen.Add(name))
                continue;
            var kept = keep(item, name);
            if (kept != null)
                result.Add(kept);
        }

        return result;
    }
}
