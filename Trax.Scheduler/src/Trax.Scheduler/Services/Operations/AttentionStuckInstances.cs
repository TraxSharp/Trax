namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// The stuck instances of one machine in one state, system instances and users' drafts together.
/// Stuck is the rule <see cref="MachineInstanceRecord.Stuck"/> states.
/// </summary>
/// <param name="Machine">The machine's id.</param>
/// <param name="State">The state they are stuck in.</param>
/// <param name="Count">How many.</param>
/// <param name="Link">The console's machine page, filtered to this state's stuck instances.</param>
public sealed record AttentionStuckInstances(string Machine, string State, long Count, string Link);
