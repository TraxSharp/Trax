using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Ingest.Contracts;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Tests.Fakes;

public enum IngestTrigger
{
    Start,
    Retry,
    Abandon,
    Approve,
}
