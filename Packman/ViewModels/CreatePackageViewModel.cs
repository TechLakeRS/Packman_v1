using Packman.Helpers;
using Packman.Models;
using Packman.Services;
using System.Diagnostics;

namespace Packman.ViewModels;

public class CreatePackageViewModel : ObservableObject
{
    private string _sourcesPath = "";
    private string _appName = "";
    private string _manufacturer = "";
    private string _version = "";
    private bool _userInstall = false;
    private string _detectedPackageType = "";
    private MsiInfoService.MsiInfo? _currentMsiInfo;
    private PSADTOptions? _currentPSADTOptions;
    private string _currentPackagePath = "";
    private string _statusText = "";
    private bool _isGenerating = false;
    private string _extractedIconPath = "";

    public string SourcesPath
    {
        get => _sourcesPath;
        set => Set(ref _sourcesPath, value);
    }

    public string AppName
    {
        get => _appName;
        set => Set(ref _appName, value);
    }

    public string Manufacturer
    {
        get => _manufacturer;
        set => Set(ref _manufacturer, value);
    }

    public string Version
    {
        get => _version;
        set => Set(ref _version, value);
    }

    public bool UserInstall
    {
        get => _userInstall;
        set => Set(ref _userInstall, value);
    }

    public string DetectedPackageType
    {
        get => _detectedPackageType;
        set => Set(ref _detectedPackageType, value);
    }

    public MsiInfoService.MsiInfo? CurrentMsiInfo
    {
        get => _currentMsiInfo;
        set => Set(ref _currentMsiInfo, value);
    }

    public PSADTOptions? CurrentPSADTOptions
    {
        get => _currentPSADTOptions;
        set => Set(ref _currentPSADTOptions, value);
    }

    public string CurrentPackagePath
    {
        get => _currentPackagePath;
        set => Set(ref _currentPackagePath, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    public bool IsGenerating
    {
        get => _isGenerating;
        set => Set(ref _isGenerating, value);
    }

    public string ExtractedIconPath
    {
        get => _extractedIconPath;
        set => Set(ref _extractedIconPath, value);
    }

    public void LoadFromFile(string filePath)
    {
        SourcesPath = filePath;
        var ext = Path.GetExtension(filePath).ToLower();
        DetectedPackageType = ext == ".msi" ? "MSI" : ext == ".exe" ? "EXE" : "Unknown";

        if (ext == ".msi")
        {
            var info = MsiInfoService.ExtractMsiInfo(filePath);
            if (info.IsValid)
            {
                CurrentMsiInfo = info;
                if (string.IsNullOrWhiteSpace(AppName)) AppName = info.ProductName;
                if (string.IsNullOrWhiteSpace(Manufacturer)) Manufacturer = info.Manufacturer;
                if (string.IsNullOrWhiteSpace(Version)) Version = info.ProductVersion;
            }
        }
        else if (ext == ".exe")
        {
            var (name, company, ver) = MetadataExtractor.ExtractExeMetadata(filePath);
            if (string.IsNullOrWhiteSpace(AppName) && !string.IsNullOrWhiteSpace(name)) AppName = name;
            if (string.IsNullOrWhiteSpace(Manufacturer) && !string.IsNullOrWhiteSpace(company)) Manufacturer = company;
            if (string.IsNullOrWhiteSpace(Version) && !string.IsNullOrWhiteSpace(ver)) Version = ver;
        }

        if (string.IsNullOrWhiteSpace(AppName))
            AppName = MetadataExtractor.ExtractNameFromFilename(filePath);

        ExtractedIconPath = IconExtractor.ExtractIconToTemp(filePath) ?? "";
    }

    public ApplicationInfo BuildApplicationInfo()
    {
        var installContext = UserInstall ? "User" : "System";
        var info = new ApplicationInfo
        {
            Name = AppName.Trim(),
            Manufacturer = string.IsNullOrWhiteSpace(Manufacturer) ? "Unknown" : Manufacturer.Trim(),
            Version = string.IsNullOrWhiteSpace(Version) ? "1.0.0" : Version.Trim(),
            SourcesPath = SourcesPath.Trim(),
            InstallContext = installContext
        };

        if (CurrentMsiInfo?.IsValid == true)
        {
            info.MsiProductCode = CurrentMsiInfo.ProductCode;
            info.MsiProductVersion = CurrentMsiInfo.ProductVersion;
            info.MsiUpgradeCode = CurrentMsiInfo.UpgradeCode;
        }

        return info;
    }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(AppName))
        {
            StatusText = "Application Name is required.";
            return false;
        }
        return true;
    }

    public async Task<string?> GenerateAsync(AppSettings settings)
    {
        if (!Validate()) return null;

        IsGenerating = true;
        StatusText = "Generating package…";

        try
        {
            var outputPath = settings.NetworkPaths.IntuneApplications;
            var templatePath = settings.NetworkPaths.PSADTTemplate;

            if (string.IsNullOrWhiteSpace(outputPath) || string.IsNullOrWhiteSpace(templatePath))
            {
                StatusText = "Configure IntuneApplications and PSADTTemplate paths in Settings first.";
                return null;
            }

            var appInfo = BuildApplicationInfo();
            var options = CurrentPSADTOptions ?? new PSADTOptions { PackageType = DetectedPackageType };

            var generator = new PSADTGenerator(outputPath, templatePath);
            var packagePath = await generator.CreatePackageAsync(appInfo, options).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(packagePath) && !string.IsNullOrEmpty(ExtractedIconPath))
                IconExtractor.CopyIconToPackage(ExtractedIconPath, packagePath, appInfo.Name);

            CurrentPackagePath = packagePath;
            StatusText = $"Package created · {DateTime.Now:HH:mm:ss}";
            Debug.WriteLine($"Package created: {packagePath}");
            return packagePath;
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            Debug.WriteLine($"Package generation failed: {ex}");
            return null;
        }
        finally
        {
            IsGenerating = false;
        }
    }

    public void Reset()
    {
        SourcesPath = "";
        AppName = "";
        Manufacturer = "";
        Version = "";
        ExtractedIconPath = "";
        UserInstall = false;
        DetectedPackageType = "";
        CurrentMsiInfo = null;
        CurrentPSADTOptions = null;
        CurrentPackagePath = "";
        StatusText = "";
    }
}
