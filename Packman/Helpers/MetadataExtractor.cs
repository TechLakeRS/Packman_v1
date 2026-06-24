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
}
