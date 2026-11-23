using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Orders;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.Recovery.Trains.OrderDemo;

public interface ITabletCallsTrain : IServiceTrain<TabletCallsInput, TabletCallsOutput>;
