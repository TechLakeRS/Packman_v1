using Packman.Services;
using Xunit;

namespace Packman.Tests;

/// <summary>
/// The computer name reaches a UNC path and a remote command line, so the validator is
/// the boundary that keeps injection out.
/// </summary>
public class RemoteTestServiceTests
{
    [Theory]
    [InlineData("PC-01")]
    [InlineData("workstation.contoso.com")]
    [InlineData("A")]
    [InlineData("host-123.sub.domain.local")]
    public void IsValidComputerName_accepts_plain_host_names(string name)
        => Assert.True(RemoteTestService.IsValidComputerName(name));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-startsWithDash")]
    [InlineData(".startsWithDot")]
    [InlineData("has space")]
    [InlineData("has;semicolon")]
    [InlineData("has'quote")]
    [InlineData("has\"quote")]
    [InlineData("has$dollar")]
    [InlineData("has`backtick")]
    [InlineData("has|pipe")]
    [InlineData("has&amp")]
    [InlineData(@"..\..\traversal")]
    [InlineData(@"\\unc\share")]
    public void IsValidComputerName_rejects_anything_that_could_change_the_command(string name)
        => Assert.False(RemoteTestService.IsValidComputerName(name));

    [Fact]
    public void IsValidComputerName_rejects_an_over_long_name()
        => Assert.False(RemoteTestService.IsValidComputerName(new string('a', 64)));

    [Theory]
    [InlineData(0)]      // success
    [InlineData(3010)]   // success, reboot required
    [InlineData(1641)]   // success, reboot initiated
    public void IsSuccess_accepts_the_PSADT_success_codes(int exitCode)
        => Assert.True(RemoteTestService.IsSuccess(exitCode));

    [Theory]
    [InlineData(1)]
    [InlineData(1603)]
    [InlineData(-1)]
    [InlineData(-2)]
    public void IsSuccess_rejects_failures(int exitCode)
        => Assert.False(RemoteTestService.IsSuccess(exitCode));

    [Theory]
    [InlineData("Install")]
    [InlineData("Uninstall")]
    [InlineData("Repair")]
    public void DeploymentTypes_contains_the_PSADT_verbs(string verb)
        => Assert.Contains(verb, RemoteTestService.DeploymentTypes);

    [Theory]
    [InlineData("install")]                       // case matters; PSADT is passed the value verbatim
    [InlineData("Install; Remove-Item C:\\ -Recurse")]
    [InlineData("")]
    public void DeploymentTypes_excludes_everything_else(string value)
        => Assert.DoesNotContain(value, RemoteTestService.DeploymentTypes);
}
