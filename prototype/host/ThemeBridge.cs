using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Otoasobi;

// 製品の AppearanceTheme はメインウィンドウの再起動に繋がる。
// 試作がリンクする PresetUi は、色を取るときだけこの二つのメソッドを呼ぶ。
static class AppearanceTheme
{
    public static ElementTheme Resolve(FrameworkElement element) => element.ActualTheme;

    public static Brush? Brush(ElementTheme theme, string key) => null;
}
