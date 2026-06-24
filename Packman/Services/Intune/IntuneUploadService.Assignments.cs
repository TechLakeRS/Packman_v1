using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Packman.Services;

public partial class IntuneUploadService
{
    /// <summary>
    /// Assigns the uploaded Win32 app to the given Entra groups with the chosen intent
    /// ("required", "available" or "uninstall"). No-op when the group list is empty.
    /// </summary>
    public async Task AssignAppToGroupsAsync(string appId, IEnumerable<string> groupIds, string intent)
    {
        var assignments = groupIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => (object)new Dictionary<string, object>
            {
                ["@odata.type"] = "#microsoft.graph.mobileAppAssignment",
                ["intent"] = intent,
                ["target"] = new Dictionary<string, object>
                {
                    ["@odata.type"] = "#microsoft.graph.groupAssignmentTarget",
                    ["groupId"] = id,
                },
            })
            .ToList();

        if (assignments.Count == 0)
            return;

        EnsureHttpClient();
        var json = JsonSerializer.Serialize(new Dictionary<string, object> { ["mobileAppAssignments"] = assignments });
        var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/assign";

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, url);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await sharedHttpClient!.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Assignment failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
    }
}
