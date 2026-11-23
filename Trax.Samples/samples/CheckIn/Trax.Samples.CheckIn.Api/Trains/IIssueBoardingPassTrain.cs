using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.CheckIn.Api.Auth;

namespace Trax.Samples.CheckIn.Trains;

public interface IIssueBoardingPassTrain : IServiceTrain<BoardingPassInput, BoardingPassOutput>;
