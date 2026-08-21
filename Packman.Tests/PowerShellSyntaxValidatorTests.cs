using Packman.Services;
using Xunit;

namespace Packman.Tests;

public class PowerShellSyntaxValidatorTests
{
    [Fact]
    public void Valid_script_reports_no_errors()
        => Assert.Empty(PowerShellSyntaxValidator.Validate("Write-Host 'hello'"));

    [Fact]
    public void Empty_script_reports_no_errors()
        => Assert.Empty(PowerShellSyntaxValidator.Validate(""));

    [Fact]
    public void Unterminated_string_is_reported()
        => Assert.NotEmpty(PowerShellSyntaxValidator.Validate("Write-Host 'unterminated"));

    [Fact]
    public void Unclosed_brace_is_reported()
        => Assert.NotEmpty(PowerShellSyntaxValidator.Validate("if ($true) {"));

    [Fact]
    public void An_unescaped_apostrophe_in_metadata_is_a_syntax_error()
    {
        // This is what PSADTGenerator would emit for the vendor "O'Reilly" without escaping.
        Assert.NotEmpty(PowerShellSyntaxValidator.Validate("$s = @{ AppVendor = 'O'Reilly' }"));
    }

    [Fact]
    public void The_escaped_form_parses()
        => Assert.Empty(PowerShellSyntaxValidator.Validate("$s = @{ AppVendor = 'O''Reilly' }"));

    [Fact]
    public void Errors_use_one_based_positions_and_never_a_zero_width_span()
    {
        var errors = PowerShellSyntaxValidator.Validate("Write-Host 'x'\nif ($true) {");

        var error = Assert.Single(errors);
        Assert.True(error.Line >= 1, "line numbers are 1-based for Monaco");
        Assert.True(error.Column >= 1, "column numbers are 1-based for Monaco");

        // Monaco draws nothing for a zero-width marker, so the validator widens them.
        if (error.EndLine == error.Line)
            Assert.True(error.EndColumn > error.Column);
    }
}
