using LanguageExt;
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
    protected override async Task<Either<Exception, InterfacelessTestOutput>> RunInternal(
        InterfacelessTestInput input
    )
    {
        await Task.CompletedTask;

        return new InterfacelessTestOutput
        {
            Id = input.Id,
            Message = $"Processed {input.Id} without a dedicated interface",
        };
    }
}
