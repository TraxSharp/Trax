using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Samples.Recovery.Trains.Ingest;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Recovery.Machines;

public enum PartitionTrigger
{
    /// <summary>Discovery's first move: run the ingest.</summary>
    Ingest,

    /// <summary>Run the ingest again after it failed or was cancelled: a new run, under a new token.</summary>
    Retry,

    /// <summary>A person let a partition held for review through.</summary>
    Approve,
}
