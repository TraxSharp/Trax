namespace Trax.Cli.Models;

public class ApiField
{
    public required string Name { get; init; }
    public required string TypeName { get; set; }
    public bool IsRequired { get; init; }
    public bool IsNullable { get; init; }
    public string? Description { get; init; }
}
