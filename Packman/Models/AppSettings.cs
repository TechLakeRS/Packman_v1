using System.Text.Json.Serialization;

namespace Packman.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuthMode { Interactive, AppRegistration }

public class AppSettings
{
    public AuthMode AuthMode { get; set; } = AuthMode.Interactive;
    public AuthConfig Authentication { get; set; } = new();
    public CodeSigningConfig CodeSigning { get; set; } = new();
    public NetworkPathsConfig NetworkPaths { get; set; } = new();

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
}
