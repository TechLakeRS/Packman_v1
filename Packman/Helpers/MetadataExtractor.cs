using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Packman.Helpers;

public static class MetadataExtractor
{
    public static (string productName, string companyName, string version) ExtractExeMetadata(string exePath)
    {
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(exePath);
            return (
                CleanProductName(vi.ProductName ?? ""),
                vi.CompanyName ?? "",
                vi.ProductVersion ?? vi.FileVersion ?? ""
            );
        }
        catch
        {
            return ("", "", "");
        }
    }

    public static string CleanProductName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        return name
            .Replace(" Setup", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" Installer", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" Installation", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    public static string ExtractNameFromFilename(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        return name
            .Replace("_setup", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Setup", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_installer", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Installer", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_install", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Install", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    public static string ExtractVersionFromFilename(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return "";
        var match = Regex.Match(fileName, @"(\d+\.){1,}\d+");
        return match.Success ? match.Value : "";
    }

    /// <summary>
    /// Extracts metadata from a PSADT deploy script (v3 or v4).
    /// v4: $adtSession = @{ AppVendor = 'value' } hashtable format.
    /// v3: $appVendor = 'value' variable format.
    /// Returns keys: Vendor, AppName, Version, ScriptDate, ScriptAuthor.
    /// </summary>
    public static Dictionary<string, string> ExtractMetadataFromScript(string scriptPath)
    {
        var metadata = new Dictionary<string, string>();

        try
        {
            var scriptContent = File.ReadAllText(scriptPath);
            var isV3 = Path.GetFileName(scriptPath).Equals("Deploy-Application.ps1", StringComparison.OrdinalIgnoreCase);

            if (isV3)
            {
                metadata["Vendor"] = ExtractPowerShellVariable(scriptContent, "appVendor") ?? "";
                metadata["AppName"] = ExtractPowerShellVariable(scriptContent, "appName") ?? "";
                metadata["Version"] = ExtractPowerShellVariable(scriptContent, "appVersion") ?? "";
                metadata["ScriptDate"] = ExtractPowerShellVariable(scriptContent, "appScriptDate") ?? "";
                metadata["ScriptAuthor"] = ExtractPowerShellVariable(scriptContent, "appScriptAuthor") ?? "";
            }
            else
            {
                metadata["Vendor"] = ExtractHashtableValue(scriptContent, "AppVendor") ?? "";
                metadata["AppName"] = ExtractHashtableValue(scriptContent, "AppName") ?? "";
                metadata["Version"] = ExtractHashtableValue(scriptContent, "AppVersion") ?? "";
                metadata["ScriptDate"] = ExtractHashtableValue(scriptContent, "AppScriptDate") ?? "";
                metadata["ScriptAuthor"] = ExtractHashtableValue(scriptContent, "AppScriptAuthor") ?? "";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error extracting metadata from script: {ex.Message}");
        }

        return metadata;
    }

    private static string? ExtractHashtableValue(string scriptContent, string keyName)
    {
        var match = Regex.Match(scriptContent, $@"^\s*{keyName}\s*=\s*['""]([^'""]*)['""]",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractPowerShellVariable(string scriptContent, string variableName)
    {
        var match = Regex.Match(scriptContent, $@"^\s*\${variableName}\s*=\s*['""]([^'""]*)['""]",
            RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }
}
