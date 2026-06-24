using System.IO;

namespace Packman.Helpers;

/// <summary>
/// Path helpers for locating PSADT package files (supports both v3 and v4 layouts).
/// </summary>
public static class FolderBrowserHelper
{
    /// <summary>
    /// Validates that a folder looks like a PSADT v4 package (Application folder
    /// with Invoke-AppDeployToolkit.exe + .ps1), or is itself the Application folder.
    /// </summary>
    public static bool ValidatePackageStructure(string packagePath)
    {
        if (string.IsNullOrEmpty(packagePath) || !Directory.Exists(packagePath))
            return false;

        var applicationFolder = Path.Combine(packagePath, "Application");
        if (!Directory.Exists(applicationFolder))
            return HasPSADTExecutable(packagePath);

        return HasPSADTExecutable(applicationFolder);
    }

    private static bool HasPSADTExecutable(string folderPath)
    {
        var v4Exe = Path.Combine(folderPath, "Invoke-AppDeployToolkit.exe");
        var v4Ps1 = Path.Combine(folderPath, "Invoke-AppDeployToolkit.ps1");
        return File.Exists(v4Exe) && File.Exists(v4Ps1);
    }

    /// <summary>
    /// Gets the PSADT v4 executable path (Invoke-AppDeployToolkit.exe) from a folder.
    /// </summary>
    public static string? GetPSADTExecutablePath(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return null;

        var v4Exe = Path.Combine(folderPath, "Invoke-AppDeployToolkit.exe");
        return File.Exists(v4Exe) ? v4Exe : null;
    }

    /// <summary>
    /// Gets the PSADT script path from a folder (v4 Invoke-AppDeployToolkit.ps1 or v3 Deploy-Application.ps1).
    /// </summary>
    public static string? GetPSADTScriptPath(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return null;

        var v4Script = Path.Combine(folderPath, "Invoke-AppDeployToolkit.ps1");
        if (File.Exists(v4Script))
            return v4Script;

        var v3Script = Path.Combine(folderPath, "Deploy-Application.ps1");
        if (File.Exists(v3Script))
            return v3Script;

        return null;
    }

    /// <summary>
    /// Determines if a script is PSADT v3 (Deploy-Application.ps1).
    /// </summary>
    public static bool IsPSADTv3Script(string scriptPath)
        => Path.GetFileName(scriptPath).Equals("Deploy-Application.ps1", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the package root, handling the case where the user selected the Application folder.
    /// </summary>
    public static string GetPackageRootPath(string selectedPath)
    {
        if (string.IsNullOrEmpty(selectedPath))
            return selectedPath;

        if (HasPSADTExecutable(selectedPath))
        {
            var parent = Directory.GetParent(selectedPath);
            if (parent != null)
                return parent.FullName;
        }

        return selectedPath;
    }
}
