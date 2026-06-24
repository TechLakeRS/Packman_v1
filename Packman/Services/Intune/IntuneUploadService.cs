using Packman.Helpers;
using Packman.Models;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;

namespace Packman.Services;

public interface IUploadProgress
{
    void UpdateProgress(int percentage, string message);
}

/// <summary>
/// Uploads a Win32 (PSADT) package to Microsoft Intune via Graph.
/// Authentication is delegated to a token provider (Packman's interactive sign-in).
/// </summary>
public partial class IntuneUploadService : IDisposable
{
    private HttpClient? sharedHttpClient;
    private readonly Func<Task<string>> _tokenProvider;
    private readonly NativeCodeSigner? _signer;
    private readonly string _converterPath;
    private string _currentAppId = "";
    private string _currentContentVersionId = "";
    private string _currentFileId = "";

    public IntuneUploadService(Func<Task<string>> tokenProvider, NativeCodeSigner? signer, string converterPath)
    {
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _signer = signer;
        _converterPath = converterPath;
    }

    private void EnsureHttpClient()
    {
        sharedHttpClient ??= new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    }

    private async Task<HttpRequestMessage> CreateAuthenticatedRequestAsync(HttpMethod method, string url)
    {
        var token = await _tokenProvider();
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    public async Task<string> UploadWin32ApplicationAsync(
        ApplicationInfo appInfo,
        string packagePath,
        List<DetectionRule> detectionRules,
        string installCommand,
        string uninstallCommand,
        string description,
        string installContext,
        string? iconPath = null,
        IUploadProgress? progress = null,
        string? predecessorAppId = null,
        AppSettings.GroupAssignmentConfig? groupAssignment = null)
    {
        using var uploadLogger = new UploadLogger(appInfo.Name);

        try
        {
            uploadLogger.Section("UPLOAD METADATA");
            uploadLogger.LogMetadata("Application Name", appInfo.Name);
            uploadLogger.LogMetadata("Manufacturer", appInfo.Manufacturer);
            uploadLogger.LogMetadata("Version", appInfo.Version);
            uploadLogger.LogMetadata("Package Path", packagePath);
            uploadLogger.LogMetadata("Install Command", installCommand);
            uploadLogger.LogMetadata("Uninstall Command", uninstallCommand);
            uploadLogger.LogMetadata("Install Context", installContext);
            uploadLogger.LogMetadata("Detection Rules", $"{detectionRules.Count} rule(s)");
            uploadLogger.Info($"Upload log file: {uploadLogger.LogFilePath}");

            uploadLogger.Section("UPLOAD PROCESS");

            progress?.UpdateProgress(5, "Authenticating with Microsoft Graph...");
            uploadLogger.Progress(5, "Authenticating with Microsoft Graph...");
            _ = await _tokenProvider();
            uploadLogger.Success("Authentication successful");
            EnsureHttpClient();

            progress?.UpdateProgress(10, "Signing application files...");
            uploadLogger.Progress(10, "Signing application files...");
            await SignApplicationFilesAsync(packagePath, progress, uploadLogger);

            progress?.UpdateProgress(20, "Packaging application files...");
            uploadLogger.Progress(20, "Packaging application files...");
            await CreateIntuneWinFileAsync(packagePath);
            uploadLogger.Success("Package created successfully");

            progress?.UpdateProgress(25, "Verifying package creation...");
            var intuneFolder = Path.Combine(packagePath, "Intune");
            var intuneWinFiles = Directory.GetFiles(intuneFolder, "*.intunewin");
            if (intuneWinFiles.Length == 0)
                throw new Exception("No .intunewin file found after conversion.");

            var intuneWinFile = intuneWinFiles[0];
            uploadLogger.Success($"Found .intunewin file: {Path.GetFileName(intuneWinFile)}");

            progress?.UpdateProgress(30, "Reading package metadata...");
            var intuneWinInfo = ExtractIntuneWinInfo(intuneWinFile);
            uploadLogger.Success($"Package metadata extracted - Size: {intuneWinInfo.UnencryptedContentSize:N0} bytes");

            progress?.UpdateProgress(35, "Registering application in Intune...");
            var appId = await CreateWin32LobAppAsync(appInfo, installCommand, uninstallCommand, description, detectionRules, installContext, intuneWinInfo, iconPath);
            uploadLogger.Success($"Application registered with ID: {appId}");

            progress?.UpdateProgress(45, "Preparing content storage...");
            var contentVersionId = await CreateContentVersionAsync(appId);
            _currentAppId = appId;
            _currentContentVersionId = contentVersionId;
            uploadLogger.Success($"Content version created: {contentVersionId}");

            progress?.UpdateProgress(55, "Initializing file upload...");
            var fileId = await CreateFileEntryAsync(appId, contentVersionId, intuneWinInfo);
            _currentFileId = fileId;
            uploadLogger.Success($"File entry created: {fileId}");

            progress?.UpdateProgress(65, "Requesting Azure upload URL...");
            var azureStorageInfo = await WaitForAzureStorageUriAsync(appId, contentVersionId, fileId);
            uploadLogger.Success("Azure Storage URI obtained");

            progress?.UpdateProgress(75, "Uploading package to Azure...");
            await UploadFileToAzureStorageAsync(azureStorageInfo.SasUri, intuneWinInfo.EncryptedFilePath, progress);
            uploadLogger.Success("Package uploaded to Azure Storage successfully");

            progress?.UpdateProgress(85, "Finalizing package upload...");
            await CommitFileAsync(appId, contentVersionId, fileId, intuneWinInfo.EncryptionInfo);
            uploadLogger.Success("File committed successfully");

            progress?.UpdateProgress(90, "Processing uploaded package...");
            await WaitForFileProcessingAsync(appId, contentVersionId, fileId, "CommitFile");
            uploadLogger.Success("File processing completed");

            progress?.UpdateProgress(95, "Publishing application...");
            await CommitAppAsync(appId, contentVersionId);
            uploadLogger.Success("Application published successfully");

            CleanupTempFiles(intuneWinInfo);

            progress?.UpdateProgress(100, "Upload complete!");
            uploadLogger.Section("UPLOAD COMPLETE");
            uploadLogger.Success($"Application '{appInfo.Name}' uploaded successfully! ID: {appId}");

            PackageMarker.SaveMarker(packagePath, appId, appInfo.Name, appInfo.Version);

            if (!string.IsNullOrEmpty(predecessorAppId))
                await WriteSupersedenceAsync(appId, predecessorAppId, uploadLogger);

            if (groupAssignment != null &&
                (groupAssignment.CreateGroupPerPackage || groupAssignment.ExistingGroups.Count > 0))
            {
                progress?.UpdateProgress(98, "Assigning groups...");
                uploadLogger.Section("GROUP ASSIGNMENT");
                await AssignGroupsAsync(appId, appInfo, groupAssignment, uploadLogger);
            }

            return appId;
        }
        catch (Exception ex)
        {
            uploadLogger.Section("UPLOAD FAILED");
            uploadLogger.Error("Upload process failed", ex);
            throw new Exception($"Failed to upload application to Intune: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        // HttpClient is long-lived; nothing to dispose here.
        GC.SuppressFinalize(this);
    }

    private async Task WriteSupersedenceAsync(string newAppId, string predecessorAppId, UploadLogger uploadLogger)
    {
        try
        {
            EnsureHttpClient();
            var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{newAppId}/updateRelationships";
            var json = $$"""
            {
              "relationships": [
                {
                  "@odata.type": "#microsoft.graph.mobileAppSupersedence",
                  "targetId": "{{predecessorAppId}}",
                  "targetType": "child",
                  "supersedenceType": "update"
                }
              ]
            }
            """;
            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, url);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await sharedHttpClient!.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                uploadLogger.Success($"Marked previous app {predecessorAppId} as superseded");
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync();
                uploadLogger.Warning($"Could not write supersedence (HTTP {(int)response.StatusCode}): {body}");
            }
        }
        catch (Exception ex)
        {
            uploadLogger.Warning($"Could not write supersedence: {ex.Message}");
        }
    }
}

public class IntuneWinInfo
{
    public string FileName { get; set; } = "";
    public long UnencryptedContentSize { get; set; }
    public string EncryptedFilePath { get; set; } = "";
    public string TempDirectory { get; set; } = "";
    public EncryptionInfo EncryptionInfo { get; set; } = new();
}

public class EncryptionInfo
{
    public string EncryptionKey { get; set; } = "";
    public string MacKey { get; set; } = "";
    public string InitializationVector { get; set; } = "";
    public string Mac { get; set; } = "";
    public string ProfileIdentifier { get; set; } = "";
    public string FileDigest { get; set; } = "";
    public string FileDigestAlgorithm { get; set; } = "";
}

public class AzureStorageInfo
{
    public string SasUri { get; set; } = "";
}
