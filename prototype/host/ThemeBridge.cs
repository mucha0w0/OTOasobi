using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Otoasobi;

// PresetUi が色を取るときだけ、この二つのメソッドを呼ぶ。
static class AppearanceTheme
{
    public static ElementTheme Resolve(FrameworkElement element) => element.ActualTheme;

    public static Brush? Brush(ElementTheme theme, string key) => null;
}
