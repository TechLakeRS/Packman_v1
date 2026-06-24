using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Packman.Services;

public partial class IntuneUploadService
{
    private async Task<string> CreateContentVersionAsync(string appId)
    {
        var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions";
        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, url);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await sharedHttpClient!.SendAsync(request);
        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to create content version. Status: {response.StatusCode}, Response: {responseText}");

        var contentVersion = JsonSerializer.Deserialize<JsonElement>(responseText);
        var contentVersionId = contentVersion.GetProperty("id").GetString();
        return contentVersionId ?? throw new Exception("Content version ID not returned");
    }

    private async Task<string> CreateFileEntryAsync(string appId, string contentVersionId, IntuneWinInfo intuneWinInfo)
    {
        var encryptedSize = new FileInfo(intuneWinInfo.EncryptedFilePath).Length;

        var fileBody = new Dictionary<string, object?>
        {
            ["@odata.type"] = "#microsoft.graph.mobileAppContentFile",
            ["name"] = intuneWinInfo.FileName,
            ["size"] = intuneWinInfo.UnencryptedContentSize,
            ["sizeEncrypted"] = encryptedSize,
            ["manifest"] = null,
            ["isDependency"] = false
        };

        var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/files";
        var json = JsonSerializer.Serialize(fileBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, url);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await sharedHttpClient!.SendAsync(request);
        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to create file entry. Status: {response.StatusCode}, Response: {responseText}");

        var fileEntry = JsonSerializer.Deserialize<JsonElement>(responseText);
        var fileId = fileEntry.GetProperty("id").GetString();
        return fileId ?? throw new Exception("File ID not returned");
    }

    private async Task<AzureStorageInfo> WaitForAzureStorageUriAsync(string appId, string contentVersionId, string fileId)
    {
        var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/files/{fileId}";

        for (int attempts = 0; attempts < 120; attempts++) // 20 minutes total
        {
            try
            {
                using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url);
                var response = await sharedHttpClient!.SendAsync(request);
                var responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Failed to get file info. Status: {response.StatusCode}, Response: {responseText}");

                var fileInfo = JsonSerializer.Deserialize<JsonElement>(responseText);

                if (!fileInfo.TryGetProperty("uploadState", out var uploadStateProp))
                {
                    await Task.Delay(10000);
                    continue;
                }

                var uploadState = uploadStateProp.GetString() ?? "";

                if (uploadState.Equals("AzureStorageUriRequestSuccess", StringComparison.OrdinalIgnoreCase))
                {
                    if (fileInfo.TryGetProperty("azureStorageUri", out var azureStorageUriProp))
                    {
                        var azureStorageUri = azureStorageUriProp.GetString();
                        return new AzureStorageInfo { SasUri = azureStorageUri ?? throw new Exception("Azure Storage URI is null") };
                    }
                    throw new Exception("Upload state is success but azureStorageUri is missing");
                }

                if (uploadState.Equals("AzureStorageUriRequestPending", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(10000);
                    continue;
                }

                if (uploadState.Equals("AzureStorageUriRequestFailed", StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Azure Storage URI request failed");

                if (uploadState.Equals("AzureStorageUriRequestTimedOut", StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Azure Storage URI request timed out");

                if (attempts < 115)
                {
                    await Task.Delay(15000);
                    continue;
                }

                throw new Exception($"Unknown upload state after many attempts: '{uploadState}'. Check Intune admin center for app status.");
            }
            catch (Exception ex) when (!(ex.Message.Contains("upload state") || ex.Message.Contains("Failed to get file info")))
            {
                Debug.WriteLine($"Network exception on attempt {attempts + 1}: {ex.Message}");
                if (attempts < 115)
                {
                    await Task.Delay(10000);
                    continue;
                }
                throw;
            }
        }

        throw new Exception("Timeout waiting for Azure Storage URI after 20 minutes. The application was created in Intune but file upload preparation timed out.");
    }

    private async Task UploadFileToAzureStorageAsync(string sasUri, string filePath, IUploadProgress? progress = null)
    {
        var fileInfo = new FileInfo(filePath);
        var totalSize = fileInfo.Length;

        int chunkSize = totalSize > 5L * 1024 * 1024 * 1024 ? 4 * 1024 * 1024 : 6 * 1024 * 1024;
        var totalChunks = (int)Math.Ceiling((double)totalSize / chunkSize);

        progress?.UpdateProgress(65, $"Preparing upload ({FormatBytes(totalSize)})...");

        using var azureHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var blockIds = new List<string>();
        var sasRenewalTimer = Stopwatch.StartNew();
        var currentSasUri = sasUri;

        for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
        {
            var blockId = Convert.ToBase64String(Encoding.ASCII.GetBytes(chunkIndex.ToString("0000")));
            blockIds.Add(blockId);

            long startPosition = (long)chunkIndex * chunkSize;
            int bytesToRead = (int)Math.Min(chunkSize, totalSize - startPosition);

            var buffer = new byte[bytesToRead];
            var totalBytesRead = 0;
            fileStream.Position = startPosition;

            while (totalBytesRead < buffer.Length)
            {
                var bytesRead = await fileStream.ReadAsync(buffer.AsMemory(totalBytesRead, buffer.Length - totalBytesRead));
                if (bytesRead == 0)
                    break;
                totalBytesRead += bytesRead;
            }

            if (totalBytesRead != buffer.Length)
                Array.Resize(ref buffer, totalBytesRead);

            var percentComplete = (int)((long)chunkIndex * 100 / totalChunks);
            var progressPercentage = 65 + (int)((chunkIndex + 1.0) / totalChunks * 15);
            progress?.UpdateProgress(progressPercentage, $"Uploading chunk {chunkIndex + 1}/{totalChunks} ({percentComplete}%)");

            if (chunkIndex < totalChunks - 1 && sasRenewalTimer.ElapsedMilliseconds >= 420000)
            {
                progress?.UpdateProgress(progressPercentage, "Renewing SAS token...");
                try
                {
                    currentSasUri = await RenewSasUriAsync();
                    sasRenewalTimer.Restart();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SAS renewal failed, continuing with current token: {ex.Message}");
                }
            }

            var chunkUri = $"{currentSasUri}&comp=block&blockid={blockId}";
            await UploadChunkWithRetryAsync(azureHttpClient, chunkUri, buffer, chunkIndex, totalChunks);
        }

        progress?.UpdateProgress(82, "Finalizing Azure upload...");
        await CommitBlockListWithRetryAsync(azureHttpClient, currentSasUri, blockIds);
        progress?.UpdateProgress(84, "Package uploaded to Azure");
    }

    private async Task UploadChunkWithRetryAsync(HttpClient client, string chunkUri, byte[] buffer, int chunkIndex, int totalChunks)
    {
        const int maxRetries = 5;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, chunkUri);
                request.Content = new ByteArrayContent(buffer);
                request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain") { CharSet = "iso-8859-1" };
                request.Headers.Add("x-ms-blob-type", "BlockBlob");

                var timeout = chunkIndex == totalChunks - 1 ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(5 + attempt);
                using var cts = new CancellationTokenSource(timeout);
                var response = await client.SendAsync(request, cts.Token);

                if (response.IsSuccessStatusCode)
                    return;

                var errorText = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Chunk {chunkIndex + 1} failed (attempt {attempt}/{maxRetries}): {response.StatusCode} {errorText}");

                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500
                    && response.StatusCode != System.Net.HttpStatusCode.RequestTimeout
                    && (int)response.StatusCode != 429)
                {
                    throw new Exception($"Client error: {response.StatusCode} - {errorText}");
                }
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine($"Chunk {chunkIndex + 1} timed out (attempt {attempt}/{maxRetries})");
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"Network error on chunk {chunkIndex + 1} (attempt {attempt}/{maxRetries}): {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error on chunk {chunkIndex + 1} (attempt {attempt}/{maxRetries}): {ex.Message}");
                if (attempt == maxRetries)
                    throw;
            }

            if (attempt < maxRetries)
            {
                var baseDelay = Math.Pow(2, attempt);
                var jitter = new Random().NextDouble();
                await Task.Delay(TimeSpan.FromSeconds(baseDelay + baseDelay * jitter));
            }
            else
            {
                throw new Exception($"Failed to upload chunk {chunkIndex + 1} after {maxRetries} attempts");
            }
        }
    }

    private async Task CommitBlockListWithRetryAsync(HttpClient client, string sasUri, List<string> blockIds)
    {
        var blockListXml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><BlockList>";
        foreach (var blockId in blockIds)
            blockListXml += $"<Latest>{blockId}</Latest>";
        blockListXml += "</BlockList>";

        const int maxRetries = 5;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var finalizeUri = $"{sasUri}&comp=blocklist";
                using var request = new HttpRequestMessage(HttpMethod.Put, finalizeUri);
                request.Content = new StringContent(blockListXml, Encoding.UTF8);
                request.Content.Headers.ContentType = null;

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                var response = await client.SendAsync(request, cts.Token);

                if (response.IsSuccessStatusCode)
                    return;

                var errorText = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Block list commit failed (attempt {attempt}/{maxRetries}): {response.StatusCode} {errorText}");
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine($"Block list commit timed out (attempt {attempt}/{maxRetries})");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error committing block list (attempt {attempt}/{maxRetries}): {ex.Message}");
            }

            if (attempt < maxRetries)
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt) * 2));
            else
                throw new Exception($"Failed to commit block list after {maxRetries} attempts");
        }
    }

    private string FormatBytes(long bytes)
    {
        if (bytes >= 1073741824) return $"{bytes / 1073741824.0:F2} GB";
        if (bytes >= 1048576) return $"{bytes / 1048576.0:F2} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F2} KB";
        return $"{bytes} bytes";
    }

    private async Task CommitFileAsync(string appId, string contentVersionId, string fileId, EncryptionInfo encryptionInfo)
    {
        var commitBody = new Dictionary<string, object>
        {
            ["fileEncryptionInfo"] = new Dictionary<string, object>
            {
                ["encryptionKey"] = encryptionInfo.EncryptionKey ?? "",
                ["macKey"] = encryptionInfo.MacKey ?? "",
                ["initializationVector"] = encryptionInfo.InitializationVector ?? "",
                ["mac"] = encryptionInfo.Mac ?? "",
                ["profileIdentifier"] = encryptionInfo.ProfileIdentifier ?? "ProfileVersion1",
                ["fileDigest"] = encryptionInfo.FileDigest ?? "",
                ["fileDigestAlgorithm"] = encryptionInfo.FileDigestAlgorithm ?? "SHA256"
            }
        };

        var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/files/{fileId}/commit";
        var json = JsonSerializer.Serialize(commitBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, url);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await sharedHttpClient!.SendAsync(request);
        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to commit file. Status: {response.StatusCode}, Response: {responseText}");
    }

    private async Task WaitForFileProcessingAsync(string appId, string contentVersionId, string fileId, string stage)
    {
        var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/files/{fileId}";
        var successState = $"{stage}Success";
        var pendingState = $"{stage}Pending";

        for (int attempts = 0; attempts < 120; attempts++)
        {
            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url);
            var response = await sharedHttpClient!.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Failed to get file processing status. Status: {response.StatusCode}, Response: {responseText}");

            var fileInfo = JsonSerializer.Deserialize<JsonElement>(responseText);

            if (!fileInfo.TryGetProperty("uploadState", out var uploadStateProp))
            {
                await Task.Delay(5000);
                continue;
            }

            var uploadState = uploadStateProp.GetString() ?? "";

            if (uploadState.Equals(successState, StringComparison.OrdinalIgnoreCase))
                return;

            if (uploadState.Equals(pendingState, StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(5000);
                continue;
            }

            if (uploadState.Equals($"{stage}Failed", StringComparison.OrdinalIgnoreCase))
                throw new Exception($"File processing failed for stage: {stage}. State: {uploadState}");

            if (uploadState.Equals($"{stage}TimedOut", StringComparison.OrdinalIgnoreCase))
                throw new Exception($"File processing timed out for stage: {stage}. State: {uploadState}");

            if (attempts < 115)
            {
                await Task.Delay(10000);
                continue;
            }

            throw new Exception($"Unknown file processing state after many attempts: '{uploadState}'. Check Intune admin center.");
        }

        throw new Exception($"Timeout waiting for file processing stage: {stage} after 10 minutes");
    }

    private async Task CommitAppAsync(string appId, string contentVersionId)
    {
        var commitBody = new Dictionary<string, object>
        {
            ["@odata.type"] = "#microsoft.graph.win32LobApp",
            ["committedContentVersion"] = contentVersionId
        };

        var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}";
        var json = JsonSerializer.Serialize(commitBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var request = await CreateAuthenticatedRequestAsync(new HttpMethod("PATCH"), url);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var sw = Stopwatch.StartNew();
        var response = await sharedHttpClient!.SendAsync(request);
        sw.Stop();

        if (response.IsSuccessStatusCode)
            return;

        var responseText = await response.Content.ReadAsStringAsync();
        await LogGraphFailureDiagnosticsAsync("CommitApp (PATCH)", request, response, sw, responseText);

        // Gateway 5xx often means the backend completed but exceeded the sync timeout.
        // Confirm by reading back the committed version before failing.
        if ((int)response.StatusCode >= 500 && (int)response.StatusCode < 600)
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            var actual = await TryGetCommittedVersionAsync(appId);
            if (actual.GetCommitted == contentVersionId)
                return;
        }

        throw new Exception($"Failed to commit app. Status: {response.StatusCode}, Response: {responseText}");
    }

    private readonly record struct VerifyResult(string? GetCommitted, bool VerifyFailed, string? FailureReason);

    private async Task<VerifyResult> TryGetCommittedVersionAsync(string appId)
    {
        try
        {
            var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}";
            using var req = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url);
            var resp = await sharedHttpClient!.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return new VerifyResult(null, true, $"HTTP {(int)resp.StatusCode}");

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("committedContentVersion", out var v))
                return new VerifyResult(v.ValueKind == JsonValueKind.String ? v.GetString() : null, false, null);
            return new VerifyResult(null, false, null);
        }
        catch (Exception ex)
        {
            return new VerifyResult(null, true, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task LogGraphFailureDiagnosticsAsync(
        string operation, HttpRequestMessage request, HttpResponseMessage response, Stopwatch stopwatch, string responseBody)
    {
        Debug.WriteLine($"=== GRAPH FAILURE: {operation} ===");
        Debug.WriteLine($"  Method/URL: {request.Method} {request.RequestUri}");
        Debug.WriteLine($"  Status: {(int)response.StatusCode} {response.StatusCode}");
        Debug.WriteLine($"  Duration: {stopwatch.Elapsed.TotalSeconds:F1}s");

        foreach (var name in new[] { "request-id", "client-request-id", "x-ms-ags-diagnostic", "Retry-After", "Date" })
        {
            if (response.Headers.TryGetValues(name, out var values))
                Debug.WriteLine($"  {name}: {string.Join(", ", values)}");
        }

        Debug.WriteLine($"  Body: {responseBody}");
        await Task.CompletedTask;
    }

    private void CleanupTempFiles(IntuneWinInfo intuneWinInfo)
    {
        try
        {
            if (Directory.Exists(intuneWinInfo.TempDirectory))
                Directory.Delete(intuneWinInfo.TempDirectory, true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to cleanup temp files: {ex.Message}");
        }
    }

    private async Task<string> RenewSasUriAsync()
    {
        var renewUrl = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{_currentAppId}/microsoft.graph.win32LobApp/contentVersions/{_currentContentVersionId}/files/{_currentFileId}/renewUpload";

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, renewUrl);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await sharedHttpClient!.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to renew SAS URI. Status: {response.StatusCode}, Response: {responseText}");
        }

        return await WaitForNewSasUriAfterRenewal();
    }

    private async Task<string> WaitForNewSasUriAfterRenewal()
    {
        var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{_currentAppId}/microsoft.graph.win32LobApp/contentVersions/{_currentContentVersionId}/files/{_currentFileId}";

        for (int attempts = 0; attempts < 30; attempts++)
        {
            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url);
            var response = await sharedHttpClient!.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Failed to get renewed SAS URI. Status: {response.StatusCode}, Response: {responseText}");

            var fileInfo = JsonSerializer.Deserialize<JsonElement>(responseText);

            if (fileInfo.TryGetProperty("uploadState", out var uploadStateProp) &&
                (uploadStateProp.GetString() ?? "").Equals("AzureStorageUriRenewalSuccess", StringComparison.OrdinalIgnoreCase) &&
                fileInfo.TryGetProperty("azureStorageUri", out var azureStorageUriProp))
            {
                var newSasUri = azureStorageUriProp.GetString();
                if (!string.IsNullOrEmpty(newSasUri))
                    return newSasUri;
            }

            await Task.Delay(10000);
        }

        throw new Exception("Timeout waiting for SAS URI renewal");
    }
}
