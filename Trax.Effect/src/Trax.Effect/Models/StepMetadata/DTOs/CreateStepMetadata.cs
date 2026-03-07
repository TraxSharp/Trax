using LanguageExt;

namespace Trax.Effect.Models.StepMetadata.DTOs;

public class CreateStepMetadata
{
    public string Name { get; set; } = null!;
    public string ExternalId { get; set; } = null!;
    public DateTime? StartTimeUtc { get; set; }
    public DateTime? EndTimeUtc { get; set; }
    public Type InputType { get; set; } = null!;
    public Type OutputType { get; set; } = null!;

    public EitherStatus State { get; set; }
}
