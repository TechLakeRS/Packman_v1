using System.Windows;
using Packman.Services;

namespace Packman;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeService.Apply(AppServices.Settings.Settings.Theme);
    }
}
