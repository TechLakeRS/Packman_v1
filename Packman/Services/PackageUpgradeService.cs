using Packman.Helpers;
using Packman.Models;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Packman.Services;

/// <summary>
/// Upgrades an existing PSADT package to a new version. Supports upgrading from
/// both v3 and v4 packages, always producing v4 output. Output base path and the
/// v4 template come from the Settings page (not appsettings.json).
/// </summary>
public class PackageUpgradeService
{
    private readonly string _baseOutputPath;
    private readonly string _templatePath;

    public PackageUpgradeService(string baseOutputPath, string templatePath)
    {
        _baseOutputPath = baseOutputPath;
        _templatePath = templatePath;
    }

    public async Task<string> UpgradePackageAsync(
        string existingPackagePath,
        string newVersion,
        string newSourcesPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var applicationFolder = Path.Combine(existingPackagePath, "Application");
            var existingScriptPath = FolderBrowserHelper.GetPSADTScriptPath(applicationFolder)
                ?? FolderBrowserHelper.GetPSADTScriptPath(existingPackagePath);

            if (string.IsNullOrEmpty(existingScriptPath) || !File.Exists(existingScriptPath))
                throw new FileNotFoundException("PSADT script not found in existing package (Invoke-AppDeployToolkit.ps1 or Deploy-Application.ps1)");

            bool isV3 = FolderBrowserHelper.IsPSADTv3Script(existingScriptPath);

            var metadata = MetadataExtractor.ExtractMetadataFromScript(existingScriptPath);
            var manufacturer = metadata.GetValueOrDefault("Vendor", "");
            var appName = metadata.GetValueOrDefault("AppName", "");

            var appFolderName = $"{manufacturer.Replace(" ", "_")}_{appName.Replace(" ", "_")}";
            var newPackagePath = Path.Combine(_baseOutputPath, appFolderName, newVersion);

            if (Directory.Exists(newPackagePath))
                throw new InvalidOperationException($"Version {newVersion} already exists for {appFolderName}. Please delete it first or choose a different version.");

            Directory.CreateDirectory(newPackagePath);

            if (isV3)
            {
                var generator = new PSADTGenerator(_baseOutputPath, _templatePath);
                var appInfo = new ApplicationInfo
                {
                    Name = appName,
                    Manufacturer = manufacturer,
                    Version = newVersion,
                    SourcesPath = newSourcesPath
                };

                var templatePackagePath = await generator.CreatePackageAsync(appInfo, true, cancellationToken);

                if (!templatePackagePath.Equals(newPackagePath, StringComparison.OrdinalIgnoreCase))
                {
                    if (Directory.Exists(newPackagePath))
                        Directory.Delete(newPackagePath, true);
                    Directory.Move(templatePackagePath, newPackagePath);
                }

                var v3ScriptContent = await File.ReadAllTextAsync(existingScriptPath, cancellationToken);
                await MigrateV3LogicToV4Async(newPackagePath, v3ScriptContent);
            }
            else
            {
                foreach (var folder in new[] { "Application", "Icon", "Intune", "NBB_Info", "Project Files" })
                    Directory.CreateDirectory(Path.Combine(newPackagePath, folder));

                await CopyApplicationFolderAsync(existingPackagePath, newPackagePath);
            }

            var newSourceFileName = await CopyNewSourceFilesAsync(newSourcesPath, newPackagePath);
            await CopyOptionalFolderAsync(existingPackagePath, newPackagePath, "Icon");
            await CopyOptionalFolderAsync(existingPackagePath, newPackagePath, "NBB_Info");

            string newMsiProductCode = "";
            if (Path.GetExtension(newSourcesPath).Equals(".msi", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var msiInfo = MsiInfoService.ExtractMsiInfo(newSourcesPath);
                    if (msiInfo.IsValid && !string.IsNullOrEmpty(msiInfo.ProductCode))
                        newMsiProductCode = msiInfo.ProductCode;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Could not extract MSI product code: {ex.Message}");
                }
            }

            await UpdateScriptForUpgradeAsync(newPackagePath, manufacturer, appName, newVersion, newSourceFileName, newMsiProductCode);

            return newPackagePath;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error upgrading package: {ex.Message}", ex);
        }
    }

    private async Task MigrateV3LogicToV4Async(string newPackagePath, string v3Content)
    {
        var v4ScriptPath = Path.Combine(newPackagePath, "Application", "Invoke-AppDeployToolkit.ps1");
        if (!File.Exists(v4ScriptPath))
            return;

        var v4Content = await File.ReadAllTextAsync(v4ScriptPath);

        var preInstallCode = ConvertV3CmdletsToV4(ExtractV3SectionCode(v3Content, "PRE-INSTALLATION"));
        var installCode = ConvertV3CmdletsToV4(ExtractV3SectionCode(v3Content, "INSTALLATION"));
        var postInstallCode = ConvertV3CmdletsToV4(ExtractV3SectionCode(v3Content, "POST-INSTALLATION"));
        var uninstallCode = ConvertV3CmdletsToV4(ExtractV3SectionCode(v3Content, "UNINSTALLATION"));
        var repairCode = ConvertV3CmdletsToV4(ExtractV3SectionCode(v3Content, "REPAIR"));

        var lines = v4Content.Split('\n').ToList();
        InjectMigratedCode(lines, "Repair", repairCode);
        InjectMigratedCode(lines, "Uninstallation", uninstallCode);
        InjectMigratedCode(lines, "Post-Installation", postInstallCode);
        InjectMigratedCode(lines, "Installation", installCode);
        InjectMigratedCode(lines, "Pre-Installation", preInstallCode);

        await File.WriteAllTextAsync(v4ScriptPath, string.Join('\n', lines));
    }

    private string ExtractV3SectionCode(string scriptContent, string sectionName)
    {
        var lines = scriptContent.Split('\n');
        var sectionCode = new StringBuilder();
        bool inSection = false;
        bool foundMarker = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();

            if (!inSection)
            {
                var sectionUpper = sectionName.ToUpper();
                if (trimmed.StartsWith("##*") && trimmed.ToUpper().Contains(sectionUpper))
                {
                    foundMarker = true;
                    continue;
                }
                if (foundMarker && trimmed.StartsWith("##*===="))
                {
                    inSection = true;
                    foundMarker = false;
                    continue;
                }
                continue;
            }

            if (trimmed.StartsWith("##*====") || (trimmed.StartsWith("##*") && trimmed.Length > 5 && !trimmed.StartsWith("##*-")))
                break;
            if (Regex.IsMatch(trimmed, @"^(ElseIf|Else)\s*", RegexOptions.IgnoreCase) && trimmed.Contains("$deploymentType"))
                break;
            if (trimmed.StartsWith("##* END SCRIPT BODY", StringComparison.OrdinalIgnoreCase))
                break;

            if (Regex.IsMatch(trimmed, @"^\[string\]\$installPhase\s*=", RegexOptions.IgnoreCase))
                continue;
            if (Regex.IsMatch(trimmed, @"^\$deploymentTypeName\s*=", RegexOptions.IgnoreCase))
                continue;

            if (sectionCode.Length == 0 && (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("##")))
                continue;

            sectionCode.AppendLine(lines[i]);
        }

        return sectionCode.ToString().TrimEnd();
    }

    private string ConvertV3CmdletsToV4(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return code;

        var cmdletMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Execute-MSI", "Start-ADTMsiProcess" },
            { "Execute-Process", "Start-ADTProcess" },
            { "Show-InstallationWelcome", "Show-ADTInstallationWelcome" },
            { "Show-InstallationProgress", "Show-ADTInstallationProgress" },
            { "Show-InstallationPrompt", "Show-ADTInstallationPrompt" },
            { "Show-InstallationRestartPrompt", "Show-ADTInstallationRestartPrompt" },
            { "Close-InstallationProgress", "Close-ADTInstallationProgress" },
            { "Show-DialogBox", "Show-ADTDialogBox" },
            { "Show-BalloonTip", "Show-ADTBalloonTip" },
            { "Get-InstalledApplication", "Get-ADTApplication" },
            { "Remove-MSIApplications", "Remove-ADTMsiApplications" },
            { "Copy-File", "Copy-ADTFile" },
            { "Remove-File", "Remove-ADTFile" },
            { "New-Folder", "New-ADTFolder" },
            { "Remove-Folder", "Remove-ADTFolder" },
            { "Copy-FileToUserProfiles", "Copy-ADTFileToUserProfiles" },
            { "Get-RegistryKey", "Get-ADTRegistryKey" },
            { "Set-RegistryKey", "Set-ADTRegistryKey" },
            { "Remove-RegistryKey", "Remove-ADTRegistryKey" },
            { "New-Shortcut", "New-ADTShortcut" },
            { "Remove-Shortcut", "Remove-ADTShortcut" },
            { "Set-ActiveSetup", "Set-ADTActiveSetup" },
            { "Get-FileVersion", "Get-ADTFileVersion" },
            { "Test-Battery", "Test-ADTBattery" },
            { "Test-NetworkConnection", "Test-ADTNetworkConnection" },
            { "Get-LoggedOnUser", "Get-ADTLoggedOnUser" },
            { "Test-ServiceExists", "Test-ADTServiceExists" },
            { "Get-FreeDiskSpace", "Get-ADTFreeDiskSpace" },
            { "Get-MsiTableProperty", "Get-ADTMsiTableProperty" },
            { "Set-MsiProperty", "Set-ADTMsiProperty" },
            { "Get-IniValue", "Get-ADTIniValue" },
            { "Set-IniValue", "Set-ADTIniValue" },
            { "Invoke-SCCMTask", "Invoke-ADTSCCMTask" },
            { "Install-SCCMSoftwareUpdates", "Install-ADTSCCMSoftwareUpdates" },
            { "Block-AppExecution", "Block-ADTAppExecution" },
            { "Unblock-AppExecution", "Unblock-ADTAppExecution" },
            { "Get-PendingReboot", "Get-ADTPendingReboot" },
            { "Set-ItemPermission", "Set-ADTItemPermission" },
            { "Get-UserProfiles", "Get-ADTUserProfiles" },
            { "Register-DLL", "Register-ADTDll" },
            { "Unregister-DLL", "Unregister-ADTDll" },
            { "Write-Log", "Write-ADTLogEntry" },
            { "Exit-Script", "Close-ADTSession" },
            { "Resolve-Error", "Resolve-ADTErrorRecord" },
            { "Get-ServiceStartMode", "Get-ADTServiceStartMode" },
            { "Set-ServiceStartMode", "Set-ADTServiceStartMode" },
            { "Get-DeferHistory", "Get-ADTDeferHistory" },
            { "Set-DeferHistory", "Set-ADTDeferHistory" },
            { "Get-UniversalDate", "Get-ADTUniversalDate" },
            { "Send-Keys", "Send-ADTKeys" },
            { "Test-PowerPoint", "Test-ADTPowerPoint" },
            { "Update-Desktop", "Update-ADTDesktop" },
            { "Update-SessionEnvironmentVariables", "Update-ADTEnvironmentPsProvider" },
            { "Invoke-RegisterOrUnregisterDLL", "Invoke-ADTRegSvr32" },
        };

        var parameterMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "-Parameters ", "-ArgumentList " },
            { "-AddParameters ", "-ArgumentList " },
            { "-CloseApps ", "-CloseProcesses " },
            { "-Source $deployAppScriptFriendlyName", "" },
        };

        var variableMap = new List<(string v3Pattern, string v4Replacement)>
        {
            (@"\$[Dd]ir[Ff]iles", "$($adtSession.DirFiles)"),
            (@"\$[Dd]ir[Ss]upport[Ff]iles", "$($adtSession.DirSupportFiles)"),
            (@"\$installPhase", "$($adtSession.InstallPhase)"),
            (@"\$deploymentTypeName", "$($adtSession.DeploymentType)"),
            (@"\$installName", "$($adtSession.InstallName)"),
            (@"\$installTitle", "$($adtSession.InstallTitle)"),
            (@"(?<!\[string\]\s*)\$appVendor(?!\s*=)", "$($adtSession.AppVendor)"),
            (@"(?<!\[string\]\s*)\$appName(?!\s*=)", "$($adtSession.AppName)"),
            (@"(?<!\[string\]\s*)\$appVersion(?!\s*=)", "$($adtSession.AppVersion)"),
            (@"(?<!\[string\]\s*)\$appArch(?!\s*=)", "$($adtSession.AppArch)"),
            (@"(?<!\[string\]\s*)\$appLang(?!\s*=)", "$($adtSession.AppLang)"),
            (@"(?<!\[string\]\s*)\$appRevision(?!\s*=)", "$($adtSession.AppRevision)"),
            (@"\$scriptDirectory", "$PSScriptRoot"),
            (@"\$mainExitCode", "0"),
        };

        var result = code;

        foreach (var (v3, v4) in cmdletMap)
            result = Regex.Replace(result, @"\b" + Regex.Escape(v3) + @"\b", v4, RegexOptions.IgnoreCase);

        foreach (var (v3Param, v4Param) in parameterMap)
            result = Regex.Replace(result, Regex.Escape(v3Param), v4Param, RegexOptions.IgnoreCase);

        foreach (var (v3Pattern, v4Replacement) in variableMap)
            result = Regex.Replace(result, v3Pattern, v4Replacement);

        result = StripV3StructuralWrappers(result);
        result = AddMigrationWarnings(result);

        return result;
    }

    private string StripV3StructuralWrappers(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return code;

        var result = new List<string>();
        foreach (var line in code.Split('\n'))
        {
            var trimmed = line.Trim();
            if (Regex.IsMatch(trimmed, @"^If\s*\(\s*\$deploymentType\s+-i(eq|ne)", RegexOptions.IgnoreCase)) continue;
            if (Regex.IsMatch(trimmed, @"^ElseIf\s*\(\s*\$deploymentType\s+-i(eq|ne)", RegexOptions.IgnoreCase)) continue;
            if (Regex.IsMatch(trimmed, @"^\[string\]\$installPhase\s*=", RegexOptions.IgnoreCase)) continue;
            if (Regex.IsMatch(trimmed, @"^\[string\]\$(appVendor|appName|appVersion|appArch|appLang|appRevision|appScriptVersion|appScriptDate|appScriptAuthor|installName|installTitle)\s*=", RegexOptions.IgnoreCase)) continue;
            if (Regex.IsMatch(trimmed, @"^\[(string|version|hashtable|int32)\]\$(deployAppScript|mainExitCode)", RegexOptions.IgnoreCase)) continue;
            if (Regex.IsMatch(trimmed, @"^\$deploymentTypeName\s*=", RegexOptions.IgnoreCase)) continue;
            if (Regex.IsMatch(trimmed, @"^##\*={10,}")) continue;
            if (Regex.IsMatch(trimmed, @"^##\*\s*(PRE-INSTALLATION|INSTALLATION|POST-INSTALLATION|UNINSTALLATION|REPAIR|END VARIABLE DECLARATION|VARIABLE DECLARATION)", RegexOptions.IgnoreCase)) continue;

            result.Add(line);
        }

        return string.Join('\n', result);
    }

    private string AddMigrationWarnings(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return code;

        var warnings = new List<string>();

        var v3CmdletPattern = @"\b(Execute|Show|Close|Get|Set|Remove|New|Copy|Test|Invoke|Block|Unblock|Write|Install|Register|Unregister)-(MSI|Process|InstallationWelcome|InstallationProgress|InstallationPrompt|DialogBox|BalloonTip|InstalledApplication|File|Folder|RegistryKey|Shortcut|ActiveSetup|FileVersion|Battery|NetworkConnection|LoggedOnUser|ServiceExists|Log|FreeDiskSpace|MsiTableProperty|MsiProperty|SCCMTask|SCCMSoftwareUpdates|AppExecution|PendingReboot|ItemPermission|UserProfiles|DLL|Error)\b";
        if (Regex.IsMatch(code, v3CmdletPattern, RegexOptions.IgnoreCase))
        {
            var uniqueCmdlets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(code, v3CmdletPattern, RegexOptions.IgnoreCase))
                uniqueCmdlets.Add(m.Value);
            if (uniqueCmdlets.Count > 0)
                warnings.Add($"\t\t## WARNING [V3 MIGRATION]: Possible unconverted v3 cmdlets detected: {string.Join(", ", uniqueCmdlets)}");
        }

        if (Regex.IsMatch(code, @"\$dirFiles|\$dirSupportFiles", RegexOptions.IgnoreCase))
            warnings.Add("\t\t## WARNING [V3 MIGRATION]: Possible unconverted $dirFiles/$dirSupportFiles references — should use $($adtSession.DirFiles)");

        if (code.Contains("$mainExitCode"))
            warnings.Add("\t\t## WARNING [V3 MIGRATION]: $mainExitCode reference found — v4 uses Close-ADTSession -ExitCode instead");

        return warnings.Count > 0 ? string.Join('\n', warnings) + "\n\n" + code : code;
    }

    private void InjectMigratedCode(List<string> lines, string sectionName, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return;

        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains($"<Perform {sectionName} tasks here>"))
            {
                var codeLines = code.Split('\n');
                lines.Insert(i + 1, "\t\t## Migrated from PSADT v3");
                for (int j = 0; j < codeLines.Length; j++)
                    lines.Insert(i + 2 + j, codeLines[j]);
                return;
            }
        }
    }

    private async Task CopyApplicationFolderAsync(string existingPackagePath, string newPackagePath)
    {
        var sourceAppFolder = Path.Combine(existingPackagePath, "Application");
        var destAppFolder = Path.Combine(newPackagePath, "Application");

        if (!Directory.Exists(sourceAppFolder))
            throw new DirectoryNotFoundException($"Source Application folder not found: {sourceAppFolder}");

        await Task.Run(() => CopyDirectorySelective(sourceAppFolder, destAppFolder, new[] { "Files" }));
    }

    private async Task CopyOptionalFolderAsync(string existingPackagePath, string newPackagePath, string folderName)
    {
        try
        {
            var source = Path.Combine(existingPackagePath, folderName);
            if (!Directory.Exists(source))
                return;
            await Task.Run(() => CopyDirectory(source, Path.Combine(newPackagePath, folderName)));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Warning: Could not copy {folderName} folder: {ex.Message}");
        }
    }

    private async Task<string> CopyNewSourceFilesAsync(string sourcePath, string packagePath)
    {
        var applicationFilesPath = Path.Combine(packagePath, "Application", "Files");
        Directory.CreateDirectory(applicationFilesPath);

        string sourceFileName = "";
        await Task.Run(() =>
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"Source file not found: {sourcePath}");
            sourceFileName = Path.GetFileName(sourcePath);
            File.Copy(sourcePath, Path.Combine(applicationFilesPath, sourceFileName), true);
        });

        return sourceFileName;
    }

    private void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), true);
        foreach (var subDir in Directory.GetDirectories(sourceDir))
            CopyDirectory(subDir, Path.Combine(destinationDir, Path.GetFileName(subDir)));
    }

    private void CopyDirectorySelective(string sourceDir, string destinationDir, string[] excludeFolders)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), true);
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var name = Path.GetFileName(subDir);
            if (excludeFolders.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            CopyDirectorySelective(subDir, Path.Combine(destinationDir, name), excludeFolders);
        }
    }

    private async Task UpdateScriptForUpgradeAsync(
        string packagePath, string manufacturer, string appName, string version,
        string newSourceFileName, string newMsiProductCode)
    {
        var scriptPath = Path.Combine(packagePath, "Application", "Invoke-AppDeployToolkit.ps1");
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException($"Invoke-AppDeployToolkit.ps1 not found: {scriptPath}");

        var scriptContent = await File.ReadAllTextAsync(scriptPath);
        scriptContent = UpdateScriptMetadataV4(scriptContent, manufacturer, appName, version);
        scriptContent = UpdateSourceFilePaths(scriptContent, newSourceFileName);

        if (!string.IsNullOrEmpty(newMsiProductCode))
            scriptContent = UpdateMsiProductCodes(scriptContent, newMsiProductCode);
        else if (!Path.GetExtension(newSourceFileName).Equals(".msi", StringComparison.OrdinalIgnoreCase))
            scriptContent = ReplaceMsiProductCodesWithPlaceholder(scriptContent);

        await File.WriteAllTextAsync(scriptPath, scriptContent);
    }

    private string UpdateScriptMetadataV4(string scriptContent, string manufacturer, string appName, string version)
    {
        var lines = scriptContent.Split('\n').ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("AppVendor") && line.Contains("="))
                lines[i] = $"    AppVendor = '{manufacturer}'";
            else if (line.StartsWith("AppName") && line.Contains("=") && !line.Contains("AppNameWithVersion"))
                lines[i] = $"    AppName = '{appName}'";
            else if (line.StartsWith("AppVersion") && line.Contains("="))
                lines[i] = $"    AppVersion = '{version}'";
            else if (line.StartsWith("AppScriptDate") && line.Contains("="))
                lines[i] = $"    AppScriptDate = '{DateTime.Now:yyyy-MM-dd}'";
            else if (line.StartsWith("AppScriptAuthor") && line.Contains("="))
                lines[i] = $"    AppScriptAuthor = '{Environment.UserName}'";
        }
        return string.Join('\n', lines);
    }

    private string UpdateSourceFilePaths(string scriptContent, string newSourceFileName)
    {
        if (string.IsNullOrWhiteSpace(scriptContent))
            return scriptContent;

        var patterns = new List<(string pattern, string replacement)>
        {
            (@"(\$adtSession\.DirFiles\\|""\$adtSession\.DirFiles\\)([^""'\s\\]+\.(exe|msi|bat|cmd))", $"$1{newSourceFileName}"),
            (@"('\$adtSession\.DirFiles\\)([^""'\s\\]+\.msi)", $"$1{newSourceFileName}")
        };

        var result = scriptContent;
        foreach (var (pattern, replacement) in patterns)
            result = Regex.Replace(result, pattern, replacement, RegexOptions.IgnoreCase);

        return result;
    }

    private string UpdateMsiProductCodes(string scriptContent, string newProductCode)
    {
        if (string.IsNullOrWhiteSpace(scriptContent) || string.IsNullOrWhiteSpace(newProductCode))
            return scriptContent;

        var productCodePattern = @"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}";
        return Regex.Replace(scriptContent, productCodePattern, newProductCode);
    }

    private string ReplaceMsiProductCodesWithPlaceholder(string scriptContent)
    {
        if (string.IsNullOrWhiteSpace(scriptContent))
            return scriptContent;

        var productCodePattern = @"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}";
        return Regex.Replace(scriptContent, productCodePattern, "{PRODUCT-CODE-PLACEHOLDER}");
    }
}
