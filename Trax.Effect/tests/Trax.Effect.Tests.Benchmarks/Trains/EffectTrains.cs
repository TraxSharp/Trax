using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Benchmarks.Models;
using Trax.Effect.Tests.Benchmarks.Steps;

namespace Trax.Effect.Tests.Benchmarks.Trains;

// --- Interfaces ---

public interface IEffectAddOneTrain : IServiceTrain<int, int>;

public interface IEffectAddThreeTrain : IServiceTrain<int, int>;

public interface IEffectTransformTrain : IServiceTrain<PersonDto, PersonEntity>;

public interface IEffectSimulatedIoTrain : IServiceTrain<int, int>;

public interface IEffectAddOneX1Train : IServiceTrain<int, int>;

public interface IEffectAddOneX3Train : IServiceTrain<int, int>;

public interface IEffectAddOneX5Train : IServiceTrain<int, int>;

public interface IEffectAddOneX10Train : IServiceTrain<int, int>;

// --- Implementations ---

public class EffectAddOneTrain : ServiceTrain<int, int>, IEffectAddOneTrain
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Task.FromResult(Activate(input).Chain<AddOneStep>().Resolve());
}

public class EffectAddThreeTrain : ServiceTrain<int, int>, IEffectAddThreeTrain
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Task.FromResult(
            Activate(input).Chain<AddOneStep>().Chain<AddOneStep>().Chain<AddOneStep>().Resolve()
        );
}

public class EffectTransformTrain : ServiceTrain<PersonDto, PersonEntity>, IEffectTransformTrain
{
    protected override Task<Either<Exception, PersonEntity>> RunInternal(PersonDto input) =>
        Task.FromResult(Activate(input).Chain<TransformStep>().Resolve());
}

public class EffectSimulatedIoTrain : ServiceTrain<int, int>, IEffectSimulatedIoTrain
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

public class EffectAddOneX1Train : ServiceTrain<int, int>, IEffectAddOneX1Train
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Task.FromResult(Activate(input).Chain<AddOneStep>().Resolve());
}

public class EffectAddOneX3Train : ServiceTrain<int, int>, IEffectAddOneX3Train
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Task.FromResult(
            Activate(input).Chain<AddOneStep>().Chain<AddOneStep>().Chain<AddOneStep>().Resolve()
        );
}

public class EffectAddOneX5Train : ServiceTrain<int, int>, IEffectAddOneX5Train
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

public class EffectAddOneX10Train : ServiceTrain<int, int>, IEffectAddOneX10Train
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
