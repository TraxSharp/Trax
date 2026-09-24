using LanguageExt;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

/// <summary>
/// Input type for the interfaceless test train.
/// </summary>
public record InterfacelessTestInput
{
    public required string Id { get; init; }
}

/// <summary>
/// Output type for the interfaceless test train.
/// </summary>
public record InterfacelessTestOutput
{
    public required string Id { get; init; }
    public required string Message { get; init; }
}

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

/// <summary>Builds the output for a train that has no dedicated interface.</summary>
internal sealed class BuildInterfacelessOutput
    : Junction<InterfacelessTestInput, InterfacelessTestOutput>
{
    public override Task<InterfacelessTestOutput> Run(InterfacelessTestInput input) =>
        Task.FromResult(
            new InterfacelessTestOutput
            {
                Id = input.Id,
                Message = $"Processed {input.Id} without a dedicated interface",
            }
        );
}
