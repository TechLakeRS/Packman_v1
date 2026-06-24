namespace Packman.Services;

public class PackageValidationResult
{
    public bool PackageExists { get; set; }
    public string ExistingPath { get; set; } = "";
    public string ProposedPath { get; set; } = "";
    public string AppFolderName { get; set; } = "";
    public string Version { get; set; } = "";
}

public enum ScriptPhase
{
    PreInstallation,
    Installation,
    PostInstallation,
    PreUninstallation,
    Uninstallation,
    PostUninstallation
}

public class PSADTFunctionEntry
{
    public string FunctionName { get; set; } = "";
    public string GeneratedCode { get; set; } = "";
}

public class PSADTOptions
{
    public Dictionary<ScriptPhase, List<PSADTFunctionEntry>> PhaseEntries { get; set; } = new()
    {
        { ScriptPhase.PreInstallation, new() },
        { ScriptPhase.Installation, new() },
        { ScriptPhase.PostInstallation, new() },
        { ScriptPhase.PreUninstallation, new() },
        { ScriptPhase.Uninstallation, new() },
        { ScriptPhase.PostUninstallation, new() },
    };

    public string PackageType { get; set; } = "";

    public int GetEnabledOptionsCount() =>
        PhaseEntries.Values.Sum(e => e.Count);

    public List<string> GetAllFunctionNames() =>
        PhaseEntries.Values.SelectMany(e => e).Select(e => e.FunctionName).ToList();

    public string GetPhaseCode(ScriptPhase phase)
    {
        if (!PhaseEntries.TryGetValue(phase, out var entries) || entries.Count == 0)
            return "";
        return string.Join("\n", entries.Select(e => e.GeneratedCode));
    }
}
