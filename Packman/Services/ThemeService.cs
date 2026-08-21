using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Packman.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Packman.Services;

/// <summary>
/// Swaps the palette dictionary at runtime. Every brush in the app resolves with
/// DynamicResource, so replacing the dictionary restyles the live window without
/// a restart. WPF UI's own theme is switched alongside it so the Fluent controls
/// and the Mica backdrop follow.
/// </summary>
public static class ThemeService
{
    /// <summary>Packman amber, handed to WPF UI so Fluent controls accent to it too.</summary>
    private static readonly Color Accent = Color.FromRgb(0xE0, 0xA0, 0x50);

    // Absolute pack URIs: a relative Source is resolved against the caller's
    // context, which is not the application root once this runs from a view model.
    private const string Dark = "pack://application:,,,/Themes/DarkTheme.xaml";
    private const string Light = "pack://application:,,,/Themes/LightTheme.xaml";

    public static void Apply(AppTheme theme)
    {
        var light = theme switch
        {
            AppTheme.Light => true,
            AppTheme.Dark => false,
            _ => SystemPrefersLight(),
        };
        var wanted = light ? Light : Dark;

        // WPF UI first: applying its theme resets the accent brushes, so the
        // amber has to go back on afterwards.
        var uiTheme = light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(uiTheme, WindowBackdropType.Mica, updateAccent: false);
        ApplicationAccentColorManager.Apply(Accent, uiTheme, systemGlassColor: false, systemAccentColor: false);

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
