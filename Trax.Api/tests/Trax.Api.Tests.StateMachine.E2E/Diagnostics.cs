using LanguageExt;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.StateMachine.E2E;

// A trivial anonymous query train. HotChocolate requires a non-empty root Query type, and the four
// stateMachine trains are all mutations, so a host that exposes only them needs at least one query. A real
// host has its own queries (or calls ExposeOperationQueries() with a scheduler); the E2E supplies this one
// so the schema builds without dragging in the scheduler.

public record PingInput
{
    public string? Value { get; init; }
}

public record PingOutput
{
    public string? Value { get; init; }
}

public interface IPing : IServiceTrain<PingInput, PingOutput> { }

[TraxAllowAnonymous]
[TraxQuery(Description = "Liveness echo; keeps the root Query type non-empty.")]
public class Ping : ServiceTrain<PingInput, PingOutput>, IPing
{
    protected override Task<Either<Exception, PingOutput>> Junctions() =>
        Chain<PingJunction>().Resolve();
}

public class PingJunction : Junction<PingInput, PingOutput>
{
    public override Task<PingOutput> Run(PingInput input) =>
        Task.FromResult(new PingOutput { Value = input.Value });
}
