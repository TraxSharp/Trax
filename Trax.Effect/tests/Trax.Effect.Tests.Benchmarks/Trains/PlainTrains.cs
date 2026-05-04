using LanguageExt;
using Trax.Core.Train;
using Trax.Effect.Tests.Benchmarks.Junctions;
using Trax.Effect.Tests.Benchmarks.Models;

namespace Trax.Effect.Tests.Benchmarks.Trains;

// --- Arithmetic trains (int -> int) ---

public class AddOneTrain : Train<int, int>
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Activate(input).Chain<AddOneJunction>().Resolve();
}

public class AddThreeTrain : Train<int, int>
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Activate(input)
            .Chain<AddOneJunction>()
            .Chain<AddOneJunction>()
            .Chain<AddOneJunction>()
            .Resolve();
}

// --- Transform train (PersonDto -> PersonEntity) ---

public class TransformTrain : Train<PersonDto, PersonEntity>
{
    protected override Task<Either<Exception, PersonEntity>> RunInternal(PersonDto input) =>
        Activate(input).Chain<TransformJunction>().Resolve();
}

// --- Simulated I/O train ---

public class SimulatedIoTrain : Train<int, int>
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Activate(input)
            .Chain<SimulatedIoJunction>()
            .Chain<SimulatedIoJunction>()
            .Chain<SimulatedIoJunction>()
            .Resolve();
}

// --- Scaling trains (parameterized by junction count) ---

public class AddOneX1Train : Train<int, int>
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Activate(input).Chain<AddOneJunction>().Resolve();
}

public class AddOneX3Train : Train<int, int>
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Activate(input)
            .Chain<AddOneJunction>()
            .Chain<AddOneJunction>()
            .Chain<AddOneJunction>()
            .Resolve();
}

public class AddOneX5Train : Train<int, int>
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Activate(input)
            .Chain<AddOneJunction>()
            .Chain<AddOneJunction>()
            .Chain<AddOneJunction>()
            .Chain<AddOneJunction>()
            .Chain<AddOneJunction>()
            .Resolve();
}

public class AddOneX10Train : Train<int, int>
{
    protected override Task<Either<Exception, int>> RunInternal(int input) =>
        Activate(input)
            .Chain<AddOneJunction>()
            .Chain<AddOneJunction>()
            .Chain<AddOneJunction>()
            .Chain<AddOneJunction>()
            .Chain<AddOneJunction>()
            .Chain<AddOneJunction>()
            .Chain<AddOneJunction>()
            .Chain<AddOneJunction>()
            .Chain<AddOneJunction>()
            .Chain<AddOneJunction>()
            .Resolve();
}
