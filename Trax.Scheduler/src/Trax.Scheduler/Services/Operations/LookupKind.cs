using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

/// <summary>What a <see cref="LookupMatch"/> is.</summary>
public enum LookupKind
{
    /// <summary>A run (a metadata row); its id is the run's id.</summary>
    Run,

    /// <summary>A manifest; its id is the manifest's id.</summary>
    Manifest,

    /// <summary>A work queue entry; its id is the entry's id.</summary>
    WorkQueueEntry,

    /// <summary>A dead letter; its id is the dead letter's id.</summary>
    DeadLetter,

    /// <summary>
    /// A state-machine instance; its id is the instance's GUID, with
    /// <see cref="LookupMatch.Machine"/>, <see cref="LookupMatch.OwnerKind"/> and
    /// <see cref="LookupMatch.RowId"/> naming the row.
    /// </summary>
    MachineInstance,

    /// <summary>A registered train; its id is the train's full name.</summary>
    Train,
}
