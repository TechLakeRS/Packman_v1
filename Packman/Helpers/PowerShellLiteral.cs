namespace Packman.Helpers;

/// <summary>
/// Escapes values that Packman writes into generated PowerShell. Metadata comes from
/// MSI tables, EXE version resources and free-text fields, so an apostrophe in a vendor
/// name ("O'Reilly") would otherwise produce a script that does not parse.
/// </summary>
public static class PowerShellLiteral
{
    /// <summary>Body of a single-quoted string, where '' is the only escape.</summary>
    public static string SingleQuoted(string? value) => (value ?? "").Replace("'", "''");

    /// <summary>Body of a double-quoted string, where the backtick escapes and $ interpolates.</summary>
    public static string DoubleQuoted(string? value) => (value ?? "")
        .Replace("`", "``")
        .Replace("\"", "`\"")
        .Replace("$", "`$");
}
