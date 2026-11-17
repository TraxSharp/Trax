using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;
using Trax.Mediator.Tests.MemoryLeak.Integration.Utils;
using Monad = Trax.Core.Monad;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

// Junction that handles large data
public class LargeDataJunction : Junction<LargeDataModel, SimpleOutput>
{
    public override Task<SimpleOutput> Run(LargeDataModel input)
    {
        // Simulate some processing
        var processedSize = input.Data.Length;
        return Task.FromResult(new SimpleOutput($"Processed {processedSize} bytes"));
    }
}
