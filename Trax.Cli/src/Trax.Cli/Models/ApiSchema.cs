namespace Trax.Cli.Models;

public class ApiSchema
{
    public required string SourceFile { get; init; }
    public required string SchemaType { get; init; }
    public List<ApiOperation> Operations { get; init; } = [];
    public List<ApiType> Types { get; init; } = [];
    public List<ApiEnum> Enums { get; init; } = [];
}
