using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.StateMachine;

public enum CheckoutTrigger
{
    Next,
    Back,
    Pay,
    Reset,
    AddItem,
}
