using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace Otoasobi;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // インストールと更新の前後で Velopack がこの exe を引数付きで起動する。
        // WinUI より前に返すと、その起動はすぐ終了する。
        VelopackApp.Build().Run();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
