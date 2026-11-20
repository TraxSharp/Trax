using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Trains.Checks;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Board.Machines;

public enum AssignmentTrigger
{
    Triage,
    SubmitRepro,
    Close,
    SubmitFix,
    SubmitChecks,
    Approve,
    SendBack,
}
