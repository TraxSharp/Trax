using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

public sealed record InvokedStepInput(string Mode, string Note);
