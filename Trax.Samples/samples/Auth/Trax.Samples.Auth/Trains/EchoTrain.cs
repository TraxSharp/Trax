using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Auth.Trains;

public record EchoInput(string Message);

public record EchoOutput(string Echoed);

public interface IEchoTrain : IServiceTrain<EchoInput, EchoOutput>;

/// <summary>
/// The public train: anyone may call <c>discover { echo }</c>, signed in or not.
/// <c>[TraxAllowAnonymous]</c> is how a GraphQL-exposed train says so; one with neither it nor
/// <c>[TraxAuthorize]</c> stops the host at startup.
/// </summary>
[TraxQuery]
[TraxAllowAnonymous]
public class EchoTrain : ServiceTrain<EchoInput, EchoOutput>, IEchoTrain
{
    protected override Task<Either<Exception, EchoOutput>> Junctions() =>
        Chain<EchoJunction>().Resolve();
}

public class EchoJunction : Junction<EchoInput, EchoOutput>
{
    public override Task<EchoOutput> Run(EchoInput input) =>
        Task.FromResult(new EchoOutput(input.Message));
}
