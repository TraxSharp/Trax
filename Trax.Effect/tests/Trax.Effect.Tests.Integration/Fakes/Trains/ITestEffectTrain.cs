using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.Fakes.Trains;

public interface ITestEffectTrain : IServiceTrain<TestEffectTrainInput, TestEffectTrain>;
