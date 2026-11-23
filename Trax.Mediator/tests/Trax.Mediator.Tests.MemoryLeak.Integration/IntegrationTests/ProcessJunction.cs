using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;
using Trax.Mediator.Tests.MemoryLeak.Integration.Utils;
using Monad = Trax.Core.Monad;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

// Simple junction that processes input
public class ProcessJunction : Junction<SimpleInput, SimpleOutput>
{
    public override Task<SimpleOutput> Run(SimpleInput input)
    {
        return Task.FromResult(new SimpleOutput($"Processed: {input.Name}"));
    }
}
