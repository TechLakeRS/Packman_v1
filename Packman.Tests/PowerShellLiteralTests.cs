using Packman.Helpers;
using Xunit;

namespace Packman.Tests;

/// <summary>
/// MSI and version-resource metadata goes straight into the generated script, so a
/// vendor like "O'Reilly" has to survive the trip.
/// </summary>
public class PowerShellLiteralTests
{
    [Theory]
    [InlineData("Mozilla", "Mozilla")]
    [InlineData("O'Reilly", "O''Reilly")]
    [InlineData("'", "''")]
    [InlineData("a'b'c", "a''b''c")]
    [InlineData("", "")]
    public void SingleQuoted_doubles_every_apostrophe(string input, string expected)
        => Assert.Equal(expected, PowerShellLiteral.SingleQuoted(input));

    [Fact]
    public void SingleQuoted_treats_null_as_empty()
        => Assert.Equal("", PowerShellLiteral.SingleQuoted(null));

    [Theory]
    [InlineData("setup.exe", "setup.exe")]
    [InlineData("a\"b", "a`\"b")]
    [InlineData("$env:TEMP", "`$env:TEMP")]
    [InlineData("back`tick", "back``tick")]
    public void DoubleQuoted_escapes_interpolation_and_quotes(string input, string expected)
        => Assert.Equal(expected, PowerShellLiteral.DoubleQuoted(input));

    [Fact]
    public void DoubleQuoted_escapes_the_backtick_first()
    {
        // Escaping the quote first would emit `" and the backtick pass would then double
        // it, turning the escape into a literal. ` + "  ->  `` + `"
        Assert.Equal("```\"", PowerShellLiteral.DoubleQuoted("`\""));
    }
}
