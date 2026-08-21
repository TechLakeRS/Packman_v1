using Packman.Helpers;
using Xunit;

namespace Packman.Tests;

/// <summary>
/// Metadata read from MSI tables and EXE version resources is written straight into the
/// generated deployment script, so a vendor like "O'Reilly" must not break it.
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
        // Order matters: escaping the quote first would produce `" and the later backtick
        // pass would double that backtick, turning the escape into a literal.
        // ` + "  ->  `` (literal backtick) + `" (literal quote)
        Assert.Equal("```\"", PowerShellLiteral.DoubleQuoted("`\""));
    }
}
