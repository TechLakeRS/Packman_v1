using System;
using System.Windows;

namespace Packman;

public partial class App : Application
{
    public static bool IsDarkTheme { get; private set; }

    public static void ApplyTheme(bool dark)
    {
        IsDarkTheme = dark;
        var dicts = Current.Resources.MergedDictionaries;
        var themeUri = dark
            ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
            : new Uri("Themes/LightTheme.xaml", UriKind.Relative);

        for (int i = 0; i < dicts.Count; i++)
        {
            var src = dicts[i].Source?.ToString() ?? string.Empty;
            if (src.Contains("LightTheme") || src.Contains("DarkTheme"))
            {
                dicts[i] = new ResourceDictionary { Source = themeUri };
                return;
            }
        }
    }
}
