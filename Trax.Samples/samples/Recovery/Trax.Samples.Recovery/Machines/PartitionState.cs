using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Samples.Recovery.Trains.Ingest;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Recovery.Machines;

public enum PartitionState
{
    /// <summary>Discovery found the partition and started its instance.</summary>
    Discovered,

    /// <summary>The ingest train is running for the partition.</summary>
    Ingesting,

    /// <summary>The partition's works are in the corpus.</summary>
    Ingested,

    /// <summary>Ingested, but the model was unsure about some works, which wait for a person.</summary>
    NeedsReview,

    /// <summary>A person looked at the works held for review and let the partition through.</summary>
    Approved,

    /// <summary>The ingest run failed, or was reaped after its host died.</summary>
    Failed,

    /// <summary>An operator cancelled the ingest run.</summary>
    Cancelled,
}
