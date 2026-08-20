using System.Text.Json.Serialization;

namespace Packman.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuthMode { Interactive, AppRegistration }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AssignmentIntent { Available, Required, Uninstall }

public class AppSettings
{
    public AuthMode AuthMode { get; set; } = AuthMode.Interactive;
    public AuthConfig Authentication { get; set; } = new();
    public CodeSigningConfig CodeSigning { get; set; } = new();
    public NetworkPathsConfig NetworkPaths { get; set; } = new();
    public GroupAssignmentConfig GroupAssignment { get; set; } = new();
    public IntuneDefaultsConfig IntuneDefaults { get; set; } = new();
    public RemoteTestConfig RemoteTest { get; set; } = new();

    public class AuthConfig
    {
        public string TenantId { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string CertificateThumbprint { get; set; } = "";
    }

    public class CodeSigningConfig
    {
        public bool Enabled { get; set; } = false;
        public string CertificateThumbprint { get; set; } = "";
        public string CertificateName { get; set; } = "";
        public string CertificateSubject { get; set; } = "";
        public string TimestampServer { get; set; } = "http://timestamp.digicert.com";
    }

    public class NetworkPathsConfig
    {
        public string IntuneApplications { get; set; } = "";
        public string PSADTTemplate { get; set; } = "";
        public string IntuneWinAppUtil { get; set; } = "";
        public string DefaultPackageBrowsePath { get; set; } = "";
    }

    public class GroupAssignmentConfig
    {
        // Create a brand-new security group for each uploaded package.
        public bool CreateGroupPerPackage { get; set; } = false;
        // Name template for the per-package group; tokens %vendor% %appName% %appVersion%.
        public string GroupNameTemplate { get; set; } = "%vendor%_%appName%_%appVersion%";
        public AssignmentIntent NewGroupIntent { get; set; } = AssignmentIntent.Required;

        // Existing groups that are always assigned to every upload.
        public List<ExistingGroupAssignment> ExistingGroups { get; set; } = new();
    }

    public class IntuneDefaultsConfig
    {
        public const string DefaultInstallCommand = "Invoke-AppDeployToolkit.exe Install";
        public const string DefaultUninstallCommand = "Invoke-AppDeployToolkit.exe Uninstall";
        public const string DefaultDisplayNameTemplate = "%vendor% %appName% %appVersion%";

        // Requirement rules pre-filled on the Create Package upload step.
        public RequirementInfo Requirements { get; set; } = new();
        // Return codes sent with every uploaded Win32 app.
        public List<ReturnCodeInfo> ReturnCodes { get; set; } = ReturnCodeInfo.Defaults();

        // Command lines Intune runs to install and uninstall the package.
        public string InstallCommand { get; set; } = DefaultInstallCommand;
        public string UninstallCommand { get; set; } = DefaultUninstallCommand;

        // Company Portal links; sent only when set.
        public string PrivacyUrl { get; set; } = "";
        public string InformationUrl { get; set; } = "";

        // Title template for the Intune app; tokens %vendor% %appName% %appVersion%.
        public string DisplayNameTemplate { get; set; } = DefaultDisplayNameTemplate;
    }

    public class RemoteTestConfig
    {
        // Test machines used before, most recent first; shown in the Remote Test picker.
        public List<string> RecentComputers { get; set; } = new();
        // Delete the staged package from the target after the run. Off by default so a
        // re-run only copies what changed.
        public bool CleanupAfterRun { get; set; } = false;
    }

    public class ExistingGroupAssignment
    {
        public string GroupName { get; set; } = "";
        public AssignmentIntent Intent { get; set; } = AssignmentIntent.Required;
    }
}
