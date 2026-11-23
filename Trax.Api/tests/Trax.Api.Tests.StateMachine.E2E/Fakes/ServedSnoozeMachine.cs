using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E.Fakes;

/// <summary><see cref="SnoozeMachine"/>, served.</summary>
[TraxMachine]
public sealed class ServedSnoozeMachine : SnoozeMachine;
