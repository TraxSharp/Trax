using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

// A consumer's namespace that starts with "System", which is not a framework namespace.
namespace Systematic.Tests;

public sealed record Filing(string Id, [property: TraxSensitive] string TaxId);

public class ServeFiling : Junction<Filing, string>
{
    public override Task<string> Run(Filing input) => Task.FromResult("served");
}

public interface IRouteFiling : IServiceTrain<Filing, string>;

public class RouteFiling : ServiceTrain<Filing, string>, IRouteFiling
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Switch<Filing, Desk>(tracks =>
                tracks
                    .When(Desk.Counter, t => t.Chain<ServeFiling>())
                    .When(Desk.Window, t => t.Chain<ServeFiling>())
            )
            .Resolve();
}
