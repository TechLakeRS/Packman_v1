using System.IO;

namespace Packman.Helpers;

/// <summary>
/// Best-effort lookup of an app's package folder on the configured network share.
/// The generator lays packages out as {root}\{Vendor}_{AppName}\{version}; the Intune
/// display name usually matches "{Vendor} {AppName}" or just "{AppName}".
/// </summary>
public static class PackageSourceLocator
{
    /// <summary>Returns the app's version folder (or newest version folder), or null when not found.</summary>
    public static string? Locate(string shareRoot, string publisher, string displayName, string version)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(shareRoot) || !Directory.Exists(shareRoot))
                return null;

            var appFolder = FindAppFolder(shareRoot, publisher, displayName);
            if (appFolder == null)
                return null;

            if (!string.IsNullOrWhiteSpace(version))
            {
                var versionFolder = Path.Combine(appFolder, version);
                if (Directory.Exists(versionFolder))
                    return versionFolder;
            }

            return Directory.GetDirectories(appFolder)
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            return null;   // unreachable share, no permission — the page shows "not found"
        }
    }

    private static string? FindAppFolder(string root, string publisher, string displayName)
    {
        foreach (var candidate in new[] { $"{publisher} {displayName}", $"{publisher}_{displayName}", displayName })
        {
            var folder = Path.Combine(root, Underscore(candidate));
            if (Directory.Exists(folder))
                return folder;
        }

        var suffix = Underscore(displayName);
        return Directory.GetDirectories(root)
            .FirstOrDefault(d => Path.GetFileName(d)!.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static string Underscore(string s) => s.Trim().Replace(' ', '_');
}
