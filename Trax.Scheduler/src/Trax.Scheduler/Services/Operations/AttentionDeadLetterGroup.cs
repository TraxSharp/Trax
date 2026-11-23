namespace Trax.Scheduler.Services.Operations;

/// <summary>The dead letters awaiting intervention that share a train and a reason.</summary>
/// <param name="TrainName">The train their manifests run (the train interface's full name).</param>
/// <param name="Reason">Why each was dead-lettered.</param>
/// <param name="Count">How many there are.</param>
/// <param name="OldestDeadLetteredAt">When the oldest of them was dead-lettered (UTC).</param>
/// <param name="Link">The console's dead letters, filtered to this train, reason and awaiting intervention.</param>
public sealed record AttentionDeadLetterGroup(
    string TrainName,
    string Reason,
    long Count,
    DateTime OldestDeadLetteredAt,
    string Link
);
