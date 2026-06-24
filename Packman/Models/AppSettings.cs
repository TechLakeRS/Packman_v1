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

    public class ExistingGroupAssignment
    {
        public string GroupName { get; set; } = "";
        public AssignmentIntent Intent { get; set; } = AssignmentIntent.Required;
    }
}
