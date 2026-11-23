using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Ingest.Contracts;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Tests.Fakes;

public enum IngestState
{
    Idle,
    Fetching,
    NeedsReview,
    Fetched,
    FetchFailed,
    Cancelled,
    Approved,
}
