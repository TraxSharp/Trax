using HotChocolate.Execution.Configuration;

namespace Trax.Samples.GraphQLClient.Schema;

/// <summary>
/// Single source of truth for the sample server's GraphQL schema. The server's <c>Program.cs</c>
/// applies it via <c>AddGraphQLServer().ConfigureSchema(...)</c>, and the client uses the same
/// delegate via <c>AddTraxGraphQLClient(PlayerSchemaConfiguration.Configure)</c>. Because both
/// sides reference the same static method, schema drift between them is impossible by
/// construction.
/// </summary>
public static class PlayerSchemaConfiguration
{
    public static void Configure(IRequestExecutorBuilder builder) =>
        builder
            .AddQueryType<PlayerQuery>()
            .AddMutationType<PlayerMutation>()
            .DisableIntrospection(disable: false);
}
