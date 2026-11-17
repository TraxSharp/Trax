using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;
using Trax.Mediator.Tests.MemoryLeak.Integration.Utils;
using Monad = Trax.Core.Monad;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

// Test train classes
public class SimpleInput(string name)
{
    public string Name { get; } = name;
    public DateTime CreatedAt { get; } = TimeProvider.System.GetUtcNow().UtcDateTime;
}
