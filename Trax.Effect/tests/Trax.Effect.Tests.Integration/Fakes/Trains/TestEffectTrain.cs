using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.Fakes.Trains;

public class TestEffectTrain : ServiceTrain<TestEffectTrainInput, TestEffectTrain>, ITestEffectTrain
{
    protected override Task<Either<Exception, TestEffectTrain>> Junctions() =>
        Task.FromResult(AddServices(this).Resolve());
}

public record TestEffectTrainInput();

public interface ITestEffectTrain : IServiceTrain<TestEffectTrainInput, TestEffectTrain> { }
