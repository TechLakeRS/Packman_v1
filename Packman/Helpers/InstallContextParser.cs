using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Packman.Helpers;

/// <summary>
/// Determines the install context (User vs System) for a PSADT v4 package by reading
/// RequireAdmin from the $adtSession hashtable in the deployment script.
/// </summary>
public static class InstallContextParser
{
    /// <summary>
    /// Returns "User" or "System" for the given package root. Falls back to "System",
    /// which is what Intune runs unless the package says otherwise.
    /// </summary>
    public static string ExtractFromPackage(string packagePath)
    {
        var scriptPath = Path.Combine(packagePath, "Application", PsadtLayout.ScriptName);
        if (!File.Exists(scriptPath))
            return "System";

        try
        {
            var match = Regex.Match(File.ReadAllText(scriptPath), @"^\s*RequireAdmin\s*=\s*\$(\w+)",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);

            return match.Success && match.Groups[1].Value.Equals("false", StringComparison.OrdinalIgnoreCase)
                ? "User"
                : "System";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error extracting install context: {ex.Message}");
            return "System";
        }
    }
}
