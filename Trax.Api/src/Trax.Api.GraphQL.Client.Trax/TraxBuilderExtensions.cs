using System.Reflection;
using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Trax.Api.GraphQL.Client.Trax;

/// <summary>
/// Trax-integration methods on <see cref="TraxGraphQLClientBuilder"/>. These layer on top
/// of the kernel registration via chained calls:
/// <code>
/// services.AddTraxGraphQLClient(uri)
///         .UseAssemblySchema(PlayerSchemaConfiguration.Configure)
///         .UseStartupValidation(typeof(Program).Assembly);
/// </code>
/// </summary>
public static class TraxBuilderExtensions
{
    /// <summary>
    /// Build the server's HotChocolate schema in-process from the same configuration
    /// delegate the server's <c>Program.cs</c> uses. Strongest schema-validation guarantee:
    /// the client validates against the exact schema the server compiles, with no network
    /// call and no file drift.
    ///
    /// Requires the consumer's process to take a binary dependency on whatever assembly
    /// defines <paramref name="configureSchema"/>. For air-gapped or non-.NET callers, use
    /// <see cref="TraxGraphQLClientBuilder.UseFileSchema"/> or the introspection default.
    /// </summary>
    public static TraxGraphQLClientBuilder UseAssemblySchema(
        this TraxGraphQLClientBuilder builder,
        Action<IRequestExecutorBuilder> configureSchema
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureSchema);

        var provider = new AssemblySchemaProvider(configureSchema);
        builder.ReplaceSchemaProvider(_ => provider);
        return builder;
    }

    /// <summary>
    /// Validate every <see cref="IGenericGraphQLClientRequest"/> in the supplied assemblies
    /// during host startup. Schema drift surfaces as a startup failure with the offending
    /// query, not a runtime 400 hours into production. The hosted service runs once on
    /// <c>StartAsync</c>; if any query fails validation, the host throws and refuses to
    /// accept traffic.
    /// </summary>
    public static TraxGraphQLClientBuilder UseStartupValidation(
        this TraxGraphQLClientBuilder builder,
        params Assembly[] assemblies
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(assemblies);
        if (assemblies.Length == 0)
            throw new ArgumentException(
                "At least one assembly must be supplied for startup validation.",
                nameof(assemblies)
            );

        builder.Services.AddHostedService(sp => new GraphQLClientStartupValidator(
            builder.ResolveValidator(sp),
            assemblies,
            typeFilter: null,
            sp.GetService<ILogger<GraphQLClientStartupValidator>>()
        ));
        return builder;
    }
}
