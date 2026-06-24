using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Packman.Helpers;

/// <summary>
/// Determines the install context (User vs System) for a PSADT package.
/// v4: reads RequireAdmin from $adtSession in Invoke-AppDeployToolkit.ps1.
/// v3: reads Toolkit_RequireAdmin from AppDeployToolkitConfig.xml.
/// </summary>
public static class InstallContextParser
{
    /// <summary>
    /// Returns "User" or "System" for the given package root. Falls back to "System".
    /// </summary>
    public static string ExtractFromPackage(string packagePath)
    {
        try
        {
            var applicationFolder = Path.Combine(packagePath, "Application");

            var v4ScriptPath = Path.Combine(applicationFolder, "Invoke-AppDeployToolkit.ps1");
            if (File.Exists(v4ScriptPath))
                return ExtractFromV4Script(v4ScriptPath);

            var configPath = Path.Combine(applicationFolder, "AppDeployToolkit", "AppDeployToolkitConfig.xml");
            if (!File.Exists(configPath))
                return "System";

            var doc = XDocument.Parse(File.ReadAllText(configPath));
            var requireAdminElement = doc.Descendants("Toolkit_RequireAdmin").FirstOrDefault();
            if (requireAdminElement != null &&
                requireAdminElement.Value.Trim().Equals("False", StringComparison.OrdinalIgnoreCase))
                return "User";

            return "System";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error extracting install context: {ex.Message}");
            return "System";
        }
    }

    private static string ExtractFromV4Script(string scriptPath)
    {
        try
        {
            var scriptContent = File.ReadAllText(scriptPath);
            var match = Regex.Match(scriptContent, @"^\s*RequireAdmin\s*=\s*\$(\w+)",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);

            if (match.Success && match.Groups[1].Value.Equals("false", StringComparison.OrdinalIgnoreCase))
                return "User";

            return "System";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error extracting install context from v4 script: {ex.Message}");
            return "System";
        }
    }
}
