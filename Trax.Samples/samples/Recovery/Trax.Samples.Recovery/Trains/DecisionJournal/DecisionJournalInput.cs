namespace Trax.Samples.Recovery.Trains.DecisionJournal;

public record DecisionJournalInput
{
    /// <summary>The run's external id, as runProgress names it. Only the caller who started the run may read it.</summary>
    public required string RunId { get; init; }
}
