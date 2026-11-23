using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E.Fakes;

/// <summary><see cref="MemoMachine"/>, served.</summary>
[TraxMachine]
public sealed class ServedMemoMachine : MemoMachine;
