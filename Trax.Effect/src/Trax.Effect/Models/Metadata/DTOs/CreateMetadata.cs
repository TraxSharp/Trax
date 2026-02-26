namespace Trax.Effect.Models.Metadata.DTOs;

public class CreateMetadata
{
    public required string Name { get; set; }

    public required string ExternalId { get; set; }

    public required dynamic? Input { get; set; }

    public long? ParentId { get; set; }

    public long? ManifestId { get; set; }
}
