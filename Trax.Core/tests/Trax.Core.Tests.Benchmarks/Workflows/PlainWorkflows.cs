using Trax.Core.Tests.Benchmarks.Models;
using Trax.Core.Tests.Benchmarks.Steps;
using Trax.Core.Train;
using LanguageExt;

namespace Trax.Core.Tests.Benchmarks.Workflows;

// --- Arithmetic workflows (int -> int) ---

public class AddOneWorkflow : Train<int, int>
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Task.FromResult(Activate(input).Chain<AddOneStep>().Resolve());
}

public class AddThreeWorkflow : Train<int, int>
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Task.FromResult(
            Activate(input).Chain<AddOneStep>().Chain<AddOneStep>().Chain<AddOneStep>().Resolve()
        );
}

// --- Transform workflow (PersonDto -> PersonEntity) ---

public class TransformWorkflow : Train<PersonDto, PersonEntity>
{
    protected override Task<Either<Exception, PersonEntity>> RunInternal(PersonDto input) =>
        Task.FromResult(Activate(input).Chain<TransformStep>().Resolve());
}

// --- Simulated I/O workflow ---

public class SimulatedIoWorkflow : Train<int, int>
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

// --- Scaling workflows (parameterized by step count) ---

public class AddOneX1Workflow : Train<int, int>
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Task.FromResult(Activate(input).Chain<AddOneStep>().Resolve());
}

public class AddOneX3Workflow : Train<int, int>
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Task.FromResult(
            Activate(input).Chain<AddOneStep>().Chain<AddOneStep>().Chain<AddOneStep>().Resolve()
        );
}

public class AddOneX5Workflow : Train<int, int>
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

public class AddOneX10Workflow : Train<int, int>
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
