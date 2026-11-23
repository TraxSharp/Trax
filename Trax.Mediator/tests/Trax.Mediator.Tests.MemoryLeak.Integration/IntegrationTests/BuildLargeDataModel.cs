using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;
using Trax.Mediator.Tests.MemoryLeak.Integration.Utils;
using Monad = Trax.Core.Monad;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// Builds the large payload the memory tests measure. It is a junction because a train declares
/// which junctions run; building the model is work, and work belongs in a junction.
/// </summary>
public class BuildLargeDataModel : Junction<VeryLargeDataModel, LargeDataModel>
{
    public override Task<LargeDataModel> Run(VeryLargeDataModel input) =>
        Task.FromResult(new LargeDataModel(input.Name, input.Data));
}
