namespace Trax.Cli.Models;

public class ApiOperation
{
    public required string Name { get; init; }
    public required OperationKind Kind { get; init; }
    public string? Description { get; init; }
    public string? Group { get; init; }
    public required ApiType InputType { get; init; }
    public required ApiType OutputType { get; init; }
    public string? HttpMethod { get; init; }
    public string? HttpPath { get; init; }
}
