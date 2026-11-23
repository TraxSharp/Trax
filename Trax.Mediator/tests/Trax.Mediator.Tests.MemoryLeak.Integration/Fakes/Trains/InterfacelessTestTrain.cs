using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

/// <summary>
/// A test train that has NO dedicated non-generic interface.
/// It only implements IServiceTrain&lt;TIn, TOut&gt; via ServiceTrain&lt;TIn, TOut&gt;.
/// This exercises the TrainRegistry fallback path where the service type
/// is the closed generic IServiceTrain&lt;,&gt; rather than a custom interface.
/// </summary>
public class InterfacelessTestTrain : ServiceTrain<InterfacelessTestInput, InterfacelessTestOutput>
{
    protected override Task<Either<Exception, InterfacelessTestOutput>> Junctions() =>
        Chain<BuildInterfacelessOutput>().Resolve();
}
