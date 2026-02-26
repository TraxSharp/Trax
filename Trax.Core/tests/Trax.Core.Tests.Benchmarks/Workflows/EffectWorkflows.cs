using Trax.Effect.Services.ServiceTrain;
using Trax.Core.Tests.Benchmarks.Models;
using Trax.Core.Tests.Benchmarks.Steps;
using LanguageExt;

namespace Trax.Core.Tests.Benchmarks.Workflows;

// --- Interfaces ---

public interface IEffectAddOneWorkflow : IServiceTrain<int, int>;

public interface IEffectAddThreeWorkflow : IServiceTrain<int, int>;

public interface IEffectTransformWorkflow : IServiceTrain<PersonDto, PersonEntity>;

public interface IEffectSimulatedIoWorkflow : IServiceTrain<int, int>;

public interface IEffectAddOneX1Workflow : IServiceTrain<int, int>;

public interface IEffectAddOneX3Workflow : IServiceTrain<int, int>;

public interface IEffectAddOneX5Workflow : IServiceTrain<int, int>;

public interface IEffectAddOneX10Workflow : IServiceTrain<int, int>;

// --- Implementations ---

public class EffectAddOneWorkflow : ServiceTrain<int, int>, IEffectAddOneWorkflow
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Task.FromResult(Activate(input).Chain<AddOneStep>().Resolve());
}

public class EffectAddThreeWorkflow : ServiceTrain<int, int>, IEffectAddThreeWorkflow
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Task.FromResult(
            Activate(input).Chain<AddOneStep>().Chain<AddOneStep>().Chain<AddOneStep>().Resolve()
        );
}

public class EffectTransformWorkflow
    : ServiceTrain<PersonDto, PersonEntity>,
        IEffectTransformWorkflow
{
    protected override Task<Either<Exception, PersonEntity>> RunInternal(PersonDto input) =>
        Task.FromResult(Activate(input).Chain<TransformStep>().Resolve());
}

public class EffectSimulatedIoWorkflow : ServiceTrain<int, int>, IEffectSimulatedIoWorkflow
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Task.FromResult(
            Activate(input)
                .Chain<SimulatedIoStep>()
                .Chain<SimulatedIoStep>()
                .Chain<SimulatedIoStep>()
                .Resolve()
        );
}

// --- Scaling variants ---

public class EffectAddOneX1Workflow : ServiceTrain<int, int>, IEffectAddOneX1Workflow
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Task.FromResult(Activate(input).Chain<AddOneStep>().Resolve());
}

public class EffectAddOneX3Workflow : ServiceTrain<int, int>, IEffectAddOneX3Workflow
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Task.FromResult(
            Activate(input).Chain<AddOneStep>().Chain<AddOneStep>().Chain<AddOneStep>().Resolve()
        );
}

public class EffectAddOneX5Workflow : ServiceTrain<int, int>, IEffectAddOneX5Workflow
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Task.FromResult(
            Activate(input)
                .Chain<AddOneStep>()
                .Chain<AddOneStep>()
                .Chain<AddOneStep>()
                .Chain<AddOneStep>()
                .Chain<AddOneStep>()
                .Resolve()
        );
}

public class EffectAddOneX10Workflow : ServiceTrain<int, int>, IEffectAddOneX10Workflow
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Task.FromResult(
            Activate(input)
                .Chain<AddOneStep>()
                .Chain<AddOneStep>()
                .Chain<AddOneStep>()
                .Chain<AddOneStep>()
                .Chain<AddOneStep>()
                .Chain<AddOneStep>()
                .Chain<AddOneStep>()
                .Chain<AddOneStep>()
                .Chain<AddOneStep>()
                .Chain<AddOneStep>()
                .Resolve()
        );
}
