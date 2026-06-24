namespace Packman.Models;

public class ApplicationInfo
{
    public string Manufacturer { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string InstallContext { get; set; } = "System";
    public string SourcesPath { get; set; } = "";
    public string MsiProductCode { get; set; } = "";
    public string MsiProductVersion { get; set; } = "";
    public string MsiUpgradeCode { get; set; } = "";

    public bool IsMsiPackage => !string.IsNullOrEmpty(MsiProductCode);

    public string PackageType
    {
        get
        {
            if (IsMsiPackage) return "MSI";
            if (!string.IsNullOrEmpty(SourcesPath))
            {
                var ext = System.IO.Path.GetExtension(SourcesPath).ToLower();
                if (ext == ".exe") return "EXE";
            }
            return "Unknown";
        }
    }
}
