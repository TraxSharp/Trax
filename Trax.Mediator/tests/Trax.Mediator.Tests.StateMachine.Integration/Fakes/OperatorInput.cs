using System.Text.Json.Nodes;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

// The machines and trains of the startup comparison per entering event: each machine declares the rule of every
// event entering its invoking state, and the host compares each one with the train's [TraxAuthorize]. None of the
// machine ids contains another fake's id, so a startup message naming one never matches another.

public sealed record OperatorInput(string Source);
