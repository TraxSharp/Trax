using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Trax.Api.GraphQL.Client.Trax;

/// <summary>
/// Hosted service that validates every <see cref="IGenericGraphQLClientRequest"/> in the
/// registered assemblies before the app accepts traffic. Schema drift surfaces as a startup
/// failure with the specific request type and validator error, rather than a 400 on the
/// first runtime call.
///
/// Wire it up via <c>builder.UseStartupValidation(assemblies)</c> on the
/// <see cref="TraxGraphQLClientBuilder"/>. The hosted service runs once on
/// <see cref="StartAsync"/>.
/// </summary>
public sealed class GraphQLClientStartupValidator : IHostedService
{
    private readonly IGraphQLClientValidator _validator;
    private readonly IReadOnlyList<Assembly> _assemblies;
    private readonly Func<Type, bool>? _typeFilter;
    private readonly ILogger<GraphQLClientStartupValidator>? _logger;

    public GraphQLClientStartupValidator(
        IGraphQLClientValidator validator,
        IReadOnlyList<Assembly> assemblies,
        Func<Type, bool>? typeFilter = null,
        ILogger<GraphQLClientStartupValidator>? logger = null
    )
    {
        _validator = validator;
        _assemblies = assemblies;
        _typeFilter = typeFilter;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation(
            "Validating outbound GraphQL queries across {Count} assembly(ies)",
            _assemblies.Count
        );

        try
        {
            await _validator
                .ValidateAssembliesAsync(_assemblies, _typeFilter, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GraphQLValidationException ex)
        {
            _logger?.LogError(
                ex,
                "Outbound GraphQL query failed schema validation at startup. Query: {Query}",
                ex.Query
            );
            throw;
        }

        _logger?.LogInformation("All outbound GraphQL queries validated successfully");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
