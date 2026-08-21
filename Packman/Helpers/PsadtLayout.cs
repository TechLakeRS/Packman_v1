namespace Packman.Helpers;

/// <summary>
/// Fixed names in a PSADT v4 package. Packman builds and consumes v4 only — the file
/// Intune is told to run (<see cref="SetupFileName"/>) and the file the packager
/// signs and edits (<see cref="ScriptName"/>) must stay in step, so both live here.
/// </summary>
public static class PsadtLayout
{
    /// <summary>Entry point Intune launches; also the value sent as setupFilePath.</summary>
    public const string SetupFileName = "Invoke-AppDeployToolkit.exe";

    /// <summary>Deployment script that carries the package metadata and install logic.</summary>
    public const string ScriptName = "Invoke-AppDeployToolkit.ps1";
}
