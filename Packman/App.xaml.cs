using System;
using System.Windows;

namespace Packman;

public partial class App : Application
{
    public static void ApplyTheme(bool dark)
    {
        var dicts = Current.Resources.MergedDictionaries;
        var themeUri = new Uri(dark ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml", UriKind.Relative);

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
