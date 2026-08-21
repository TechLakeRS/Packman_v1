namespace Packman.Helpers;

/// <summary>Escaping for values Packman puts inside OData $filter expressions.</summary>
public static class OData
{
    /// <summary>Body of a single-quoted OData string literal, where '' is the escape.</summary>
    public static string Literal(string? value) => (value ?? "").Replace("'", "''");
}
