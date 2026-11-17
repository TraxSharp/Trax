using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;
using Trax.Mediator.Tests.MemoryLeak.Integration.Utils;
using Monad = Trax.Core.Monad;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

// Junction that processes SimpleOutput to SimpleOutput (for chaining)
public class ProcessOutputJunction : Junction<SimpleOutput, SimpleOutput>
{
    public override Task<SimpleOutput> Run(SimpleOutput input)
    {
        return Task.FromResult(new SimpleOutput($"Reprocessed: {input.Result}"));
    }
}
