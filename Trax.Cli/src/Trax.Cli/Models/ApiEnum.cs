namespace Trax.Cli.Models;

public class ApiEnum
{
    public required string Name { get; init; }
    public required List<string> Values { get; init; }
    public string? Description { get; init; }

    /// <summary>The name as the schema writes it, before conversion; named in a collision refusal.</summary>
    internal string? SourceName { get; init; }
}
