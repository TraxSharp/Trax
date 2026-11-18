using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Api.Tests;

/// <summary>
/// Extension to call AddTraxGraphQL without full setup for isolated testing of registration logic.
/// </summary>
internal static class TestGraphQLExtensions
{
    internal static IServiceCollection AddTraxGraphQLForTesting(this IServiceCollection services)
    {
        // Call the real extension method. The operations namespace is opt-in;
        // these registration tests do not care about schema shape, but RootQuery
        // would otherwise be empty and AddTraxGraphQL would refuse the build.
        return Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
            services,
            graphql => graphql.ExposeOperationQueries().AllowAnonymousOperations()
        );
    }
}
