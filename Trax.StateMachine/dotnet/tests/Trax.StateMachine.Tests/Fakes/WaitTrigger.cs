using System.Text.Json.Nodes;
using Ingest.Contracts;

namespace Trax.StateMachine.Tests.Fakes;

public enum WaitTrigger
{
    Start,
    Retry,
}
