using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.PersistedOperations.Trains.Users.LookupUser.Junctions;

namespace Trax.Samples.PersistedOperations.Trains.Users.LookupUser;

/// <summary>
/// Resolves a user profile by id. Exposed as
/// <c>discover { users { lookupUser(input: { userId: ... }) { ... } } }</c>.
/// </summary>
[TraxAllowAnonymous]
[TraxQuery(Namespace = GraphQLNamespaces.Users, Description = "Looks up a user profile by id")]
public class LookupUserTrain : ServiceTrain<LookupUserInput, UserProfile>, ILookupUserTrain
{
    protected override Task<Either<Exception, UserProfile>> Junctions() =>
        Chain<FetchUserJunction>().Resolve();
}
