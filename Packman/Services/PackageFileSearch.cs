using Packman.Helpers;
using System.IO;

namespace Packman.Services;

/// <summary>One match: either a file whose name matched, or a matching line inside one.</summary>
public sealed record SearchHit(string Path, string Name, int Line, string Preview)
{
    public string LineLabel => Line > 0 ? $"line {Line}" : "file name";
}

/// <summary>
/// Finds text in the files of a built package, for the script editor's search box.
/// Pure file-system work with no UI, so it runs off the UI thread and is testable.
/// </summary>
public static class PackageFileSearch
{
    /// <summary>Extensions the editor treats as text, and therefore searches line by line.</summary>
    public static readonly IReadOnlySet<string> TextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".ps1", ".psm1", ".psd1", ".txt", ".xml", ".json", ".cmd", ".bat",
        ".ini", ".md", ".config", ".log", ".reg", ".csv", ".yml", ".yaml"
    };

    public static readonly IReadOnlySet<string> PowerShellExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".ps1", ".psm1", ".psd1"
    };

    private const long MaxSearchFileBytes = 2 * 1024 * 1024;
    private const int MaxHits = 200;
    private const int MaxHitsPerFile = 5;

    /// <summary>Matches file names first, then line contents of the text files under the folder.</summary>
    public static List<SearchHit> Search(string folder, string query, CancellationToken token)
    {
        var hits = new List<SearchHit>();

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return hits;
        }
        catch (DirectoryNotFoundException)
        {
            return hits;
        }

        foreach (var path in files)
        {
            token.ThrowIfCancellationRequested();
            if (hits.Count >= MaxHits) break;
            if (path.EndsWith(TextFileIO.TempSuffix, StringComparison.OrdinalIgnoreCase)) continue;

            var name = Path.GetFileName(path);
            if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
                hits.Add(new SearchHit(path, name, 0, path));

            if (!TextExtensions.Contains(Path.GetExtension(path))) continue;

            try
            {
                if (new FileInfo(path).Length > MaxSearchFileBytes) continue;

                var lineNumber = 0;
                var perFile = 0;
                foreach (var line in File.ReadLines(path))
                {
                    lineNumber++;
                    if (!line.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                    hits.Add(new SearchHit(path, name, lineNumber, line.Trim()));
                    if (++perFile >= MaxHitsPerFile || hits.Count >= MaxHits) break;
                }
            }
            catch (IOException) { }
        }

        return hits;
    }
}
