using Trax.Api.Auth;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Auth.Auth;
using Trax.Samples.Auth.Data;

namespace Trax.Samples.Auth.Trains;

/// <summary>
/// Editors whose email is verified. The policy and the role are two attributes, and they AND:
/// Erin holds <c>Editor</c> but fails <c>VerifiedEmail</c>, so she is refused.
/// </summary>
/// <remarks>
/// <c>GraphQLOperation.Run</c> only: the junction reads the caller from the request, which a
/// queued run executed later by the scheduler does not have.
/// </remarks>
[TraxMutation(GraphQLOperation.Run, Namespace = "news")]
[TraxAuthorize(AuthPolicies.VerifiedEmail)]
[TraxAuthorize(Roles = AuthRoles.Editor)]
public class PublishArticleTrain
    : ServiceTrain<PublishArticleInput, PublishArticleOutput>,
        IPublishArticleTrain
{
    protected override Task<Either<Exception, PublishArticleOutput>> Junctions() =>
        Chain<SaveArticleJunction>().Resolve();
}
