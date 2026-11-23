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

// Two worked-example machines, authored with the fluent API. A host discovers them with one line
// (trax.AddStateMachines(...) in the AddTrax chain), each is marked [TraxMachine] so the host serves it, and a client starts an instance and moves it
// through each machine's own typed operations under the `machines` GraphQL root. Both are authored declaratively,
// which is what lets them be served typed. The turnstile is the pure-structure proof (no effect); the checkout is
// the effectful proof (a committed state and one irreversible charge, fired exactly once).

public enum TurnstileState
{
    Locked,
    Unlocked,
}
