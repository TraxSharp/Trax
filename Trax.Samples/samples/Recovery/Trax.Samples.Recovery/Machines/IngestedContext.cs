using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Samples.Recovery.Trains.Ingest;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Recovery.Machines;

/// <summary>
/// An ingested partition: which one, and what the ingest wrote, as counts and a fingerprint. Pointers,
/// never rows: the works are in <c>topic_map.ingested_works</c>.
/// </summary>
public sealed record IngestedContext
{
    [MinLength(1)]
    public string Source { get; init; } = "";

    [MinLength(1)]
    public string Month { get; init; } = "";

    [MinLength(1)]
    public string Fingerprint { get; init; } = "";

    public int Works { get; init; }

    public int Created { get; init; }

    public int Merged { get; init; }

    public int NeedsReview { get; init; }
}
