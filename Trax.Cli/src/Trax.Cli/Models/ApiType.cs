namespace Trax.Cli.Models;

public class ApiType
{
    public required string Name { get; init; }
    public List<ApiField> Fields { get; init; } = [];
    public bool IsBuiltIn { get; init; }
}
