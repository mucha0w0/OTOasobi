namespace Otoasobi;

// プリセット名の判定。画面のダイアログは配布画面側にある。
static class PresetUi
{
    public static string? BasicNameError(string raw)
    {
        var name = raw.Trim();
        if (name.Length == 0)
            return "名前を入力してください。";
        if (name.Length > 40)
            return "名前は40文字までです。";
        if (name.Any(char.IsControl))
            return "名前に使えない文字があります。";
        return null;
    }

    public static string? ReservedNameError(
        string raw,
        Func<string, bool> reserved,
        string reservedMessage = "その名前は使えません。")
    {
        var basic = BasicNameError(raw);
        if (basic != null)
            return basic;
        if (reserved(raw.Trim()))
            return reservedMessage;
        return null;
    }
}
