using Trax.Core.Functional;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.Utils;

public class UnitTrain : Train<Trax.Core.Functional.Unit, Trax.Core.Functional.Unit>
{
    protected override Task<Either<Exception, Trax.Core.Functional.Unit>> Junctions() =>
        Task.FromResult(Resolve());

    public static UnitTrain Create() => new();
}
