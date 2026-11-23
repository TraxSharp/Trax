using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Cli.Tests.Fakes;

public enum TurnstileTrigger
{
    Coin,
    Push,
}
