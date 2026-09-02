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
    private readonly Lazy<Task<ISchema>> _schema;

    public AssemblySchemaProvider(Action<IRequestExecutorBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configure = configure;
        _schema = new Lazy<Task<ISchema>>(
            LoadSchemaAsync,
            LazyThreadSafetyMode.ExecutionAndPublication
        );
    }

    public Task<ISchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
        _schema.Value;

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
            return Schema.For(sdl);
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
