using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Machines;
using Trax.Samples.Board.Tickets;
using Trax.StateMachine.Persistence;
using static Trax.Samples.Board.Shop.ShippingRules;

namespace Trax.Samples.Board.Trains.Checks;

public interface IQaTrain : IServiceTrain<QaCheck, CheckResult>;
