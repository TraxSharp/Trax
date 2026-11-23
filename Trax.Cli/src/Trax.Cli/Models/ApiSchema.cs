namespace Trax.Cli.Models;

public class ApiSchema
{
    public required string SourceFile { get; init; }
    public required string SchemaType { get; init; }
    public IList<ApiOperation> Operations { get; init; } = [];
    public IList<ApiType> Types { get; init; } = [];
    public IList<ApiEnum> Enums { get; init; } = [];
}
