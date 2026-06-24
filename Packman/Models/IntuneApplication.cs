namespace Packman.Models;

/// <summary>Common properties shared by the list and detail application models.</summary>
public abstract class ApplicationBase
{
    public string DisplayName { get; set; } = "";
    public string Version { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>
/// Lightweight model for the Applications list. Mirrors the reference suite: only the
/// cheap fields returned by a single mobileApps query (plus categories) are carried —
/// status/assignments live on <see cref="ApplicationDetail"/> and are fetched on demand.
/// </summary>
public class IntuneApplication : ApplicationBase
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public DateTime LastModified { get; set; }

    // ── UI helpers for the design's table rendering ──
    private static readonly string[] Palette =
        ["#e0552b", "#3b67f5", "#1f8a5b", "#6b8a1f", "#e08a2b", "#2d8cff", "#d4322b", "#7c3aed"];

    public string Initial => string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName.Substring(0, 1).ToUpperInvariant();
    public string TileColor => Palette[(uint)DisplayName.GetHashCode() % Palette.Length];
    public string UpdatedText => RelativeTime(LastModified);

    protected static string RelativeTime(DateTime when)
    {
        if (when == default) return "—";
        var delta = DateTime.Now - when;
        if (delta.TotalSeconds < 60) return "just now";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes} min ago";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours} hour{Plural(delta.TotalHours)} ago";
        if (delta.TotalDays < 7) return $"{(int)delta.TotalDays} day{Plural(delta.TotalDays)} ago";
        if (delta.TotalDays < 30) return $"{(int)(delta.TotalDays / 7)} week{Plural(delta.TotalDays / 7)} ago";
        if (delta.TotalDays < 365) return $"{(int)(delta.TotalDays / 30)} month{Plural(delta.TotalDays / 30)} ago";
        return $"{(int)(delta.TotalDays / 365)} year{Plural(delta.TotalDays / 365)} ago";
    }

    private static string Plural(double n) => (int)n == 1 ? "" : "s";
}
