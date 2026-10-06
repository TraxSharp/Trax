using GraphQL.Types;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Api.GraphQL.Client.Trax;

/// <summary>
/// Builds the server's HotChocolate schema in-process from a configuration delegate, prints
/// it to SDL, and hands the SDL to graphql-dotnet so the validator can use it. The delegate
/// is typically the same helper the server's <c>Program.cs</c> uses, so the client and server
/// cannot drift from a shared source of truth.
///
/// Requires the consumer's process to take a binary dependency on whatever assembly defines
/// the schema configuration. For air-gapped or non-.NET callers, use <see cref="FileSchemaProvider"/>
/// or <see cref="IntrospectingSchemaProvider"/> instead.
/// </summary>
public sealed class AssemblySchemaProvider : ISchemaProvider
{
    private readonly Action<IRequestExecutorBuilder> _configure;
    private readonly bool _removeSubscriptions;
    private readonly RetryingAsyncLazy<ISchema> _schema;

    /// <summary>
    /// Creates a provider that builds the schema from <paramref name="configure"/> when it is first
    /// requested, and drops the subscription type, the client's default.
    /// </summary>
    /// <param name="configure">Configures a HotChocolate request executor builder the same way the server does.</param>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <c>null</c>.</exception>
    public AssemblySchemaProvider(Action<IRequestExecutorBuilder> configure)
        : this(configure, removeSubscriptionsFromSchema: true) { }

    /// <summary>Creates a provider that builds the schema from <paramref name="configure"/> when it is first requested.</summary>
    /// <param name="configure">Configures a HotChocolate request executor builder the same way the server does.</param>
    /// <param name="removeSubscriptionsFromSchema">
    /// Whether the subscription type is dropped before queries are validated, as
    /// <see cref="IGraphQLClientConfiguration.RemoveSubscriptionsFromSchema"/> says.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <c>null</c>.</exception>
    public AssemblySchemaProvider(
        Action<IRequestExecutorBuilder> configure,
        bool removeSubscriptionsFromSchema
    )
        : this(configure, removeSubscriptionsFromSchema, TimeProvider.System) { }

    internal AssemblySchemaProvider(
        Action<IRequestExecutorBuilder> configure,
        bool removeSubscriptionsFromSchema,
        TimeProvider time
    )
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configure = configure;
        _removeSubscriptions = removeSubscriptionsFromSchema;
        _schema = new RetryingAsyncLazy<ISchema>(LoadSchemaAsync, time);
    }

    /// <summary>
    /// Returns the schema, loading it on the first call and sharing the result. A load that
    /// fails is not kept: it is loaded again once a capped, jittered backoff has passed, and a call
    /// before then gets the failure. <paramref name="cancellationToken"/>
    /// cancels this caller's wait, not a load other callers share.
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting for the schema.</param>
    public Task<ISchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
        _schema.GetValueAsync(cancellationToken);

    private async Task<ISchema> LoadSchemaAsync()
    {
        var services = new ServiceCollection();
        var builder = services.AddGraphQL();
        _configure(builder);

        await using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<IRequestExecutorProvider>();
        var executor = await resolver.GetExecutorAsync().ConfigureAwait(false);
        var hcSchema = executor.Schema;

        var sdl = hcSchema.ToString();

        try
        {
            return SdlSchema.Build(sdl, _removeSubscriptions);
        }
        catch (Exception ex)
        {
            throw new GraphQLSchemaIntrospectionException(
                "Failed to build graphql-dotnet schema from HotChocolate-derived SDL. Generated SDL:\n"
                    + sdl,
                ex
            );
        }
    }
}
