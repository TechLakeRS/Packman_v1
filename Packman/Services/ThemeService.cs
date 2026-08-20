using System.Windows;
using Microsoft.Win32;
using Packman.Models;

namespace Packman.Services;

/// <summary>
/// Swaps the palette dictionary at runtime. Every brush in the app resolves with
/// DynamicResource, so replacing the dictionary restyles the live window without
/// a restart.
/// </summary>
public static class ThemeService
{
    // Absolute pack URIs: a relative Source is resolved against the caller's
    // context, which is not the application root once this runs from a view model.
    private const string Dark = "pack://application:,,,/Themes/DarkTheme.xaml";
    private const string Light = "pack://application:,,,/Themes/LightTheme.xaml";

    public static void Apply(AppTheme theme)
    {
        var wanted = theme switch
        {
            AppTheme.Light => Light,
            AppTheme.Dark => Dark,
            _ => SystemPrefersLight() ? Light : Dark,
        };

        var merged = Application.Current?.Resources.MergedDictionaries;
        if (merged is null) return;

        for (var i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source?.OriginalString ?? "";
            var isPalette = source.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase)
                         || source.EndsWith("LightTheme.xaml", StringComparison.OrdinalIgnoreCase);
            if (!isPalette) continue;
            // Compare on the file name: App.xaml declares a relative source, while
            // anything applied since is an absolute pack URI.
            var wantedFile = wanted[(wanted.LastIndexOf('/') + 1)..];
            if (source.EndsWith(wantedFile, StringComparison.OrdinalIgnoreCase)) return;

            merged[i] = new ResourceDictionary { Source = new Uri(wanted, UriKind.Absolute) };
            return;
        }
    }

    /// <summary>Windows records the app-theme preference as a DWORD; unreadable means dark.</summary>
    private static bool SystemPrefersLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch { return false; }
    }
}
