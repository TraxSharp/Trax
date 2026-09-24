using LanguageExt;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.Utils;

public class UnitTrain : Train<LanguageExt.Unit, LanguageExt.Unit>
{
    protected override Task<Either<Exception, LanguageExt.Unit>> Junctions() =>
        Task.FromResult(Resolve());

    public static UnitTrain Create() => new();
}
