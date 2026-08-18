namespace Packman.Helpers;

/// <summary>
/// Builds a name (Entra group, Intune app title) from a template using %vendor%,
/// %appName% and %appVersion% tokens (case-insensitive).
/// </summary>
public static class GroupAssignmentNamer
{
    public static string Build(string template, string vendor, string appName, string version)
    {
        if (string.IsNullOrWhiteSpace(template)) return "";
        return template
            .Replace("%vendor%", vendor, System.StringComparison.OrdinalIgnoreCase)
            .Replace("%appName%", appName, System.StringComparison.OrdinalIgnoreCase)
            .Replace("%appVersion%", version, System.StringComparison.OrdinalIgnoreCase)
            .Trim();
    }
}
