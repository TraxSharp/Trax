using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.PersistedOperations.Trains.Greeting.Greet.Junctions;

namespace Trax.Samples.PersistedOperations.Trains.Greeting.Greet;

/// <summary>
/// Trivial query train that produces a greeting for a name. Exposed as
/// <c>discover { greeting { greet(input: { name: ... }) { ... } } }</c>.
/// The persisted-operation manifest binds an id (e.g. <c>greet_v1</c>) to
/// a GraphQL document that calls this train.
/// </summary>
[TraxAllowAnonymous]
[TraxQuery(Namespace = GraphQLNamespaces.Greeting, Description = "Builds a greeting for a name")]
public class GreetTrain : ServiceTrain<GreetInput, GreetOutput>, IGreetTrain
{
    protected override Task<Either<Exception, GreetOutput>> Junctions() =>
        Chain<ComposeGreetingJunction>().Resolve();
}
