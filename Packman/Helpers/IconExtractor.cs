using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Packman.Helpers;

public static class IconExtractor
{
    private const string TempFolderName = "Packman";

    public static string? ExtractIconToTemp(string sourceFilePath)
    {
        if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath))
            return null;

        try
        {
            var icon = Icon.ExtractAssociatedIcon(sourceFilePath);
            if (icon == null) return null;

            var tempPath = Path.Combine(Path.GetTempPath(), TempFolderName);
            Directory.CreateDirectory(tempPath);

            var iconPath = Path.Combine(tempPath, $"{Path.GetFileNameWithoutExtension(sourceFilePath)}_icon.png");
            using (var bitmap = icon.ToBitmap())
                bitmap.Save(iconPath, ImageFormat.Png);

            return iconPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Icon extraction failed: {ex.Message}");
            return null;
        }
    }

    public static bool CopyIconToPackage(string extractedIconPath, string packageFolderPath, string appName)
    {
        if (string.IsNullOrEmpty(extractedIconPath) || !File.Exists(extractedIconPath)) return false;
        if (string.IsNullOrEmpty(packageFolderPath) || !Directory.Exists(packageFolderPath)) return false;

        try
        {
            var iconFolder = Path.Combine(packageFolderPath, "Icon");
            if (!Directory.Exists(iconFolder)) return false;

            var sanitized = SanitizeFileName(appName);
            File.Copy(extractedIconPath, Path.Combine(iconFolder, $"{sanitized}_icon.png"), overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Icon copy failed: {ex.Message}");
            return false;
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "app";
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrEmpty(sanitized) ? "app" : sanitized;
    }
}
