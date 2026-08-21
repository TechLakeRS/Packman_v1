using System.Management.Automation.Language;

namespace Packman.Services;

/// <summary>A parse error in the 1-based line/column coordinates Monaco expects.</summary>
public record SyntaxError(int Line, int Column, int EndLine, int EndColumn, string Message);

/// <summary>
/// Reports PowerShell syntax errors using the parser that ships with the runtime.
/// The script is only parsed — nothing in it is executed.
/// </summary>
public static class PowerShellSyntaxValidator
{
    public static List<SyntaxError> Validate(string script)
    {
        Parser.ParseInput(script, out _, out var errors);

        return errors.Select(e =>
        {
            var line = e.Extent.StartLineNumber;
            var column = e.Extent.StartColumnNumber;
            var endLine = e.Extent.EndLineNumber;
            var endColumn = e.Extent.EndColumnNumber;

            // Some errors report a zero-width extent, which Monaco would draw as nothing.
            if (endLine == line && endColumn <= column) endColumn = column + 1;

            return new SyntaxError(line, column, endLine, endColumn, e.Message);
        }).ToList();
    }
}
