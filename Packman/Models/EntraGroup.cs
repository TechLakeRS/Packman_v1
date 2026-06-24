namespace Packman.Models;

/// <summary>An Entra (Azure AD) security group returned by a directory search.</summary>
public class EntraGroup
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
}
