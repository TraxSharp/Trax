using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

[TraxMachine]
public sealed class GetEventMachine() : ReservedNameMachine<GetNamedTrigger>("get-event");
