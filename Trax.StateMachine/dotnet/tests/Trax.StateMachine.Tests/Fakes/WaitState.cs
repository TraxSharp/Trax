using System.Text.Json.Nodes;
using Ingest.Contracts;

namespace Trax.StateMachine.Tests.Fakes;

public enum WaitState
{
    Idle,
    Fetching,
    Fetched,
    FetchFailed,
    Cancelled,
    TimedOut,
}
