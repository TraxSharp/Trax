using LanguageExt;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.Utils;

public class UnitWorkflow : Train<LanguageExt.Unit, LanguageExt.Unit>
{
    protected override async Task<Either<Exception, LanguageExt.Unit>> RunInternal(
        LanguageExt.Unit input
    ) => Activate(input).Resolve();

    public static UnitWorkflow Create() => new();
}
