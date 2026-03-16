namespace Trax.Cli.Models;

public class ApiEnum
{
    public required string Name { get; init; }
    public required List<string> Values { get; init; }
    public string? Description { get; init; }
}
