using System.IO;
using Packman.Helpers;
using Packman.Services;
using Xunit;

namespace Packman.Tests;

public sealed class PackageFileSearchTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("packman-search").FullName;

    public PackageFileSearchTests()
    {
        File.WriteAllText(Path.Combine(_dir, "Invoke-AppDeployToolkit.ps1"),
            "AppVendor = 'Contoso'\nStart-ADTMsiProcess -Action 'Install'\n");
        Directory.CreateDirectory(Path.Combine(_dir, "Files"));
        File.WriteAllText(Path.Combine(_dir, "Files", "readme.txt"), "install notes\n");
        File.WriteAllBytes(Path.Combine(_dir, "Files", "payload.msi"), new byte[] { 1, 2, 3 });
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private List<SearchHit> Search(string query) =>
        PackageFileSearch.Search(_dir, query, CancellationToken.None);

    [Fact]
    public void Finds_a_matching_line_in_a_text_file()
    {
        var hit = Assert.Single(Search("Contoso"));
        Assert.Equal("Invoke-AppDeployToolkit.ps1", hit.Name);
        Assert.Equal(1, hit.Line);
        Assert.Equal("AppVendor = 'Contoso'", hit.Preview);
    }

    [Fact]
    public void Searches_subdirectories()
        => Assert.Contains(Search("install notes"), h => h.Name == "readme.txt");

    [Fact]
    public void Matches_file_names_and_labels_them_as_such()
    {
        var hit = Assert.Single(Search("payload"));
        Assert.Equal(0, hit.Line);
        Assert.Equal("file name", hit.LineLabel);
    }

    [Fact]
    public void Does_not_read_the_contents_of_binary_files()
    {
        // .msi is not a text extension, so its bytes are never scanned.
        Assert.DoesNotContain(Search(""), h => h.Line > 0);
    }

    [Fact]
    public void Matching_is_case_insensitive()
        => Assert.NotEmpty(Search("contoso"));

    [Fact]
    public void Line_hits_report_a_one_based_line_number()
    {
        var hit = Assert.Single(Search("Start-ADTMsiProcess"));
        Assert.Equal(2, hit.Line);
        Assert.Equal("line 2", hit.LineLabel);
    }

    [Fact]
    public void Skips_the_transient_file_a_save_writes()
    {
        File.WriteAllText(Path.Combine(_dir, "Invoke-AppDeployToolkit.ps1" + TextFileIO.TempSuffix), "Contoso");

        Assert.DoesNotContain(Search("Contoso"), h => h.Path.EndsWith(TextFileIO.TempSuffix));
    }

    [Fact]
    public void A_missing_folder_returns_nothing_rather_than_throwing()
        => Assert.Empty(PackageFileSearch.Search(
            Path.Combine(_dir, "does-not-exist"), "anything", CancellationToken.None));

    [Fact]
    public void Cancellation_is_observed()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => PackageFileSearch.Search(_dir, "Contoso", cts.Token));
    }
}
