namespace Trax.Effect.Models.DeadLetter.DTOs;

public class CreateDeadLetter
{
    public required Manifest.Manifest Manifest { get; set; }
    public required string Reason { get; set; }
    public required int RetryCount { get; set; }
}
