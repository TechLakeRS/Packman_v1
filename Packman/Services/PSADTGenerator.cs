using Packman.Models;
using System.Diagnostics;

namespace Packman.Services;

public class PSADTGenerator
{
    private readonly string _baseOutputPath;
    private readonly string _templatePath;

    public PSADTGenerator(string baseOutputPath, string templatePath)
    {
        _baseOutputPath = baseOutputPath;
        _templatePath = templatePath;
    }

    public PackageValidationResult ValidatePackageCreation(ApplicationInfo appInfo)
    {
        var appFolderName = $"{appInfo.Manufacturer.Replace(" ", "_")}_{appInfo.Name.Replace(" ", "_")}";
        var packagePath = Path.Combine(_baseOutputPath, appFolderName, appInfo.Version);
        return new PackageValidationResult
        {
            PackageExists = Directory.Exists(packagePath),
            ExistingPath = packagePath,
            ProposedPath = packagePath,
            AppFolderName = appFolderName,
            Version = appInfo.Version
        };
    }

    public async Task<string> CreatePackageAsync(ApplicationInfo appInfo, PSADTOptions? options = null,
        bool overwriteExisting = false, CancellationToken cancellationToken = default)
    {
        var appFolderName = $"{appInfo.Manufacturer.Replace(" ", "_")}_{appInfo.Name.Replace(" ", "_")}";
        var packagePath = Path.Combine(_baseOutputPath, appFolderName, appInfo.Version);

        if (Directory.Exists(packagePath))
        {
            if (!overwriteExisting)
                throw new InvalidOperationException(
                    $"Package version {appInfo.Version} already exists for {appFolderName}.");
            Directory.Delete(packagePath, true);
            await Task.Delay(200, cancellationToken);
        }

        Directory.CreateDirectory(Path.Combine(_baseOutputPath, appFolderName));
        await CopyTemplateFolderAsync(packagePath, cancellationToken);

        if (!string.IsNullOrWhiteSpace(appInfo.SourcesPath))
            await CopySourceFilesAsync(appInfo.SourcesPath, packagePath, cancellationToken);

        await ModifyScriptAsync(packagePath, appInfo, options, cancellationToken);

        Debug.WriteLine($"Package created at: {packagePath}");
        return packagePath;
    }

    private string ResolveTemplatePath()
    {
        var toTry = new List<string>();
        if (!string.IsNullOrWhiteSpace(_templatePath))
            toTry.Add(_templatePath);

        foreach (var basePath in toTry)
        {
            if (!Directory.Exists(basePath)) continue;
            var resolved = TryResolve(basePath);
            if (resolved != null) return resolved;
        }

        throw new DirectoryNotFoundException(
            $"PSADT template not found. Searched: '{_templatePath}'. " +
            "Set PSADTTemplate in appsettings.json to the folder containing Application, Icon, Intune subfolders.");
    }

    private string? TryResolve(string basePath)
    {
        if (File.Exists(Path.Combine(basePath, "Invoke-AppDeployToolkit.ps1")))
        {
            var parent = Directory.GetParent(basePath)?.FullName;
            if (parent != null && Directory.Exists(Path.Combine(parent, "Application")))
                return parent;
        }
        if (Directory.Exists(Path.Combine(basePath, "Application")))
            return basePath;

        foreach (var sub in Directory.GetDirectories(basePath).OrderByDescending(d => d))
            if (Directory.Exists(Path.Combine(sub, "Application")))
                return sub;

        return null;
    }

    private async Task CopyTemplateFolderAsync(string packagePath, CancellationToken ct)
    {
        var template = ResolveTemplatePath();
        await Task.Run(() =>
        {
            Directory.CreateDirectory(packagePath);
            foreach (var folder in new[] { "Icon", "Intune", "NBB_Info", "Project Files" })
            {
                var src = Path.Combine(template, folder);
                var dst = Path.Combine(packagePath, folder);
                if (Directory.Exists(src)) CopyDir(src, dst);
                else Directory.CreateDirectory(dst);
            }
            var appSrc = Path.Combine(template, "Application");
            if (!Directory.Exists(appSrc))
                throw new DirectoryNotFoundException($"Application folder not found in template: {template}");
            CopyDir(appSrc, Path.Combine(packagePath, "Application"));
        }, ct);
    }

    private async Task CopySourceFilesAsync(string sourcesPath, string packagePath, CancellationToken ct)
    {
        var dest = Path.Combine(packagePath, "Application", "Files");
        Directory.CreateDirectory(dest);
        await Task.Run(() =>
        {
            if (sourcesPath.Contains(';'))
            {
                foreach (var p in sourcesPath.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var t = p.Trim();
                    if (File.Exists(t)) File.Copy(t, Path.Combine(dest, Path.GetFileName(t)), true);
                }
            }
            else if (File.Exists(sourcesPath))
                File.Copy(sourcesPath, Path.Combine(dest, Path.GetFileName(sourcesPath)), true);
            else if (Directory.Exists(sourcesPath))
                CopyDir(sourcesPath, dest);
        }, ct);
    }

    private async Task ModifyScriptAsync(string packagePath, ApplicationInfo appInfo,
        PSADTOptions? options, CancellationToken ct)
    {
        var scriptPath = Path.Combine(packagePath, "Application", "Invoke-AppDeployToolkit.ps1");
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException($"Invoke-AppDeployToolkit.ps1 not found in: {packagePath}");

        var content = await File.ReadAllTextAsync(scriptPath, ct);
        content = UpdateMetadata(content, appInfo);
        content = InjectInstallCommands(content, appInfo);
        if (options != null) content = InjectFunctions(content, options);
        await File.WriteAllTextAsync(scriptPath, content, ct);
    }

    private string UpdateMetadata(string content, ApplicationInfo appInfo)
    {
        var lines = content.Split('\n').ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("AppVendor") && line.Contains("="))
                lines[i] = $"    AppVendor = '{appInfo.Manufacturer}'";
            else if (line.StartsWith("AppName") && line.Contains("=") && !line.Contains("AppNameWithVersion"))
                lines[i] = $"    AppName = '{appInfo.Name}'";
            else if (line.StartsWith("AppVersion") && line.Contains("="))
                lines[i] = $"    AppVersion = '{appInfo.Version}'";
            else if (line.StartsWith("AppScriptDate") && line.Contains("="))
                lines[i] = $"    AppScriptDate = '{DateTime.Now:MM/dd/yyyy}'";
            else if (line.StartsWith("AppScriptAuthor") && line.Contains("="))
                lines[i] = $"    AppScriptAuthor = '{Environment.UserName}'";
        }
        return string.Join('\n', lines);
    }

    private string InjectInstallCommands(string content, ApplicationInfo appInfo)
    {
        var lines = content.Split('\n').ToList();
        var sourceFileName = !string.IsNullOrEmpty(appInfo.SourcesPath)
            ? Path.GetFileName(appInfo.SourcesPath) : null;

        int installIdx = FindSection(lines, "Installation");
        if (installIdx > 0)
        {
            string code = appInfo.PackageType == "MSI"
                ? $"\n## MSI Installation\nStart-ADTMsiProcess -Action 'Install' -FilePath \"$($adtSession.DirFiles)\\{sourceFileName ?? appInfo.Name + ".msi"}\""
                : $"\n## EXE Installation\nStart-ADTProcess -FilePath \"$($adtSession.DirFiles)\\{sourceFileName ?? "setup.exe"}\" -ArgumentList '<silent flags>'";
            Insert(lines, installIdx, code);
        }

        int uninstallIdx = FindSection(lines, "Uninstallation");
        if (uninstallIdx > 0)
        {
            string code = appInfo.PackageType == "MSI"
                ? $"\n## Uninstall MSI\nStart-ADTMsiProcess -Action 'Uninstall' -FilePath '{(string.IsNullOrEmpty(appInfo.MsiProductCode) ? "{ProductCode}" : appInfo.MsiProductCode)}'"
                : $"\n## Uninstall EXE\nStart-ADTProcess -FilePath \"$($adtSession.DirFiles)\\{sourceFileName ?? "setup.exe"}\" -ArgumentList '<uninstall flags>'";
            Insert(lines, uninstallIdx, code);
        }

        return string.Join('\n', lines);
    }

    private string InjectFunctions(string content, PSADTOptions options)
    {
        var lines = content.Split('\n').ToList();
        var phaseMap = new Dictionary<ScriptPhase, string>
        {
            { ScriptPhase.PreInstallation, "Pre-Installation" },
            { ScriptPhase.Installation, "Installation" },
            { ScriptPhase.PostInstallation, "Post-Installation" },
            { ScriptPhase.PreUninstallation, "Pre-Uninstallation" },
            { ScriptPhase.Uninstallation, "Uninstallation" },
            { ScriptPhase.PostUninstallation, "Post-Uninstallation" },
        };

        foreach (var phase in phaseMap.Keys.Reverse())
        {
            var code = options.GetPhaseCode(phase);
            if (string.IsNullOrWhiteSpace(code)) continue;
            int idx = FindSection(lines, phaseMap[phase]);
            if (idx > 0) Insert(lines, idx, code);
        }

        return string.Join('\n', lines);
    }

    private int FindSection(List<string> lines, string name)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains($"<Perform {name} tasks here>") ||
                lines[i].Contains($"## {name}") ||
                lines[i].Contains($"## <{name}>"))
                return i + 1;
        }
        return -1;
    }

    private void Insert(List<string> lines, int index, string code)
    {
        var codeLines = code.Split('\n');
        for (int i = 0; i < codeLines.Length; i++)
            lines.Insert(index + i, codeLines[i]);
    }

    private void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src))
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
