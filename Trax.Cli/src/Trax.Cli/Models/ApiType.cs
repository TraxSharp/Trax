namespace Trax.Cli.Models;

public class ApiType
{
    public required string Name { get; init; }
    public List<ApiField> Fields { get; init; } = [];
    public bool IsBuiltIn { get; init; }

    /// <summary>The name as the schema writes it, before conversion; named in a collision refusal.</summary>
    internal string? SourceName { get; init; }
}
