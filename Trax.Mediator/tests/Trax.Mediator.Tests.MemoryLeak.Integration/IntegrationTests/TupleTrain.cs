using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;
using Trax.Mediator.Tests.MemoryLeak.Integration.Utils;
using Monad = Trax.Core.Monad;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

public class TupleTrain : Train<SimpleInput, (string Result, int Count, DateTime Timestamp)>
{
    protected override Task<
        Either<Exception, (string Result, int Count, DateTime Timestamp)>
    > Junctions() => Chain<TupleJunction>().Resolve();
}
