using LanguageExt;
using Trax.Core.Train;
using Trax.Effect.Tests.Benchmarks.Models;
using Trax.Effect.Tests.Benchmarks.Steps;

namespace Trax.Effect.Tests.Benchmarks.Trains;

// --- Arithmetic trains (int -> int) ---

public class AddOneTrain : Train<int, int>
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Task.FromResult(Activate(input).Chain<AddOneStep>().Resolve());
}

public class AddThreeTrain : Train<int, int>
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Task.FromResult(
            Activate(input).Chain<AddOneStep>().Chain<AddOneStep>().Chain<AddOneStep>().Resolve()
        );
}

// --- Transform train (PersonDto -> PersonEntity) ---

public class TransformTrain : Train<PersonDto, PersonEntity>
{
    protected override Task<Either<Exception, PersonEntity>> RunInternal(PersonDto input) =>
        Task.FromResult(Activate(input).Chain<TransformStep>().Resolve());
}

// --- Simulated I/O train ---

public class SimulatedIoTrain : Train<int, int>
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

// --- Scaling trains (parameterized by step count) ---

public class AddOneX1Train : Train<int, int>
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Task.FromResult(Activate(input).Chain<AddOneStep>().Resolve());
}

public class AddOneX3Train : Train<int, int>
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Task.FromResult(
            Activate(input).Chain<AddOneStep>().Chain<AddOneStep>().Chain<AddOneStep>().Resolve()
        );
}

public class AddOneX5Train : Train<int, int>
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

public class AddOneX10Train : Train<int, int>
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
