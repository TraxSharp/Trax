using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;
using Trax.Mediator.Tests.MemoryLeak.Integration.Utils;
using Monad = Trax.Core.Monad;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

// Junction that returns a tuple
public class TupleJunction : Junction<SimpleInput, (string Result, int Count, DateTime Timestamp)>
{
    public override Task<(string Result, int Count, DateTime Timestamp)> Run(SimpleInput input)
    {
        return Task.FromResult(
            (
                Result: $"Tuple result for {input.Name}",
                Count: input.Name.Length,
                Timestamp: DateTime.UtcNow
            )
        );
    }
}
