using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>What a client starts an <see cref="ExpiringMachine"/> with: nothing.</summary>
public sealed record ExpiringStart;
