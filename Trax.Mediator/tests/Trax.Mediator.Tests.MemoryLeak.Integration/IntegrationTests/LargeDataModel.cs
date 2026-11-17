using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;
using Trax.Mediator.Tests.MemoryLeak.Integration.Utils;
using Monad = Trax.Core.Monad;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

public class LargeDataModel(string name, byte[] data)
{
    public string Name { get; } = name;
    public byte[] Data { get; } = data;
    public DateTime CreatedAt { get; } = TimeProvider.System.GetUtcNow().UtcDateTime;
}
