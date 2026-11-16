using System.Globalization;
using System.Windows;
namespace MouseBridge.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        CultureInfo.DefaultThreadCurrentUICulture=CultureInfo.GetCultureInfo("en-US");
        base.OnStartup(e);
    }
}
