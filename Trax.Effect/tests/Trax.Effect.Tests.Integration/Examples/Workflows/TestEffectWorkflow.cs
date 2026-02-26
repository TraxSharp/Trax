using Trax.Effect.Services.ServiceTrain;
using LanguageExt;

namespace Trax.Effect.Tests.Integration.Examples.Workflows;

public class TestEffectWorkflow
    : ServiceTrain<TestEffectWorkflowInput, TestEffectWorkflow>,
        ITestEffectWorkflow
{
    protected override async Task<Either<Exception, TestEffectWorkflow>> RunInternal(
        TestEffectWorkflowInput input
    ) => Activate(input, this).Resolve();
}

public record TestEffectWorkflowInput();

public interface ITestEffectWorkflow
    : IServiceTrain<TestEffectWorkflowInput, TestEffectWorkflow> { }
