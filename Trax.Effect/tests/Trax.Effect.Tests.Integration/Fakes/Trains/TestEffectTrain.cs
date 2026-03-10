using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.Fakes.Trains;

public class TestEffectTrain : ServiceTrain<TestEffectTrainInput, TestEffectTrain>, ITestEffectTrain
{
    protected override async Task<Either<Exception, TestEffectTrain>> RunInternal(
        TestEffectTrainInput input
    ) => Activate(input, this).Resolve();
}

public record TestEffectTrainInput();

public interface ITestEffectTrain : IServiceTrain<TestEffectTrainInput, TestEffectTrain> { }
