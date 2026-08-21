using System.IO;
using Packman.Helpers;
using Xunit;

namespace Packman.Tests;

public sealed class InstallContextParserTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("packman-context").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string PackageWithScript(string scriptBody)
    {
        var package = Path.Combine(_dir, Guid.NewGuid().ToString("N"));
        var application = Path.Combine(package, "Application");
        Directory.CreateDirectory(application);
        File.WriteAllText(Path.Combine(application, PsadtLayout.ScriptName), scriptBody);
        return package;
    }

    [Fact]
    public void RequireAdmin_false_means_a_user_install()
        => Assert.Equal("User", InstallContextParser.ExtractFromPackage(
            PackageWithScript("$adtSession = @{\n    RequireAdmin = $false\n}")));

    [Fact]
    public void RequireAdmin_true_means_a_system_install()
        => Assert.Equal("System", InstallContextParser.ExtractFromPackage(
            PackageWithScript("$adtSession = @{\n    RequireAdmin = $true\n}")));

    [Fact]
    public void A_script_without_RequireAdmin_falls_back_to_system()
        => Assert.Equal("System", InstallContextParser.ExtractFromPackage(
            PackageWithScript("$adtSession = @{\n    AppName = 'Reader'\n}")));

    [Fact]
    public void A_missing_script_falls_back_to_system()
        => Assert.Equal("System", InstallContextParser.ExtractFromPackage(Path.Combine(_dir, "nothing-here")));
}
