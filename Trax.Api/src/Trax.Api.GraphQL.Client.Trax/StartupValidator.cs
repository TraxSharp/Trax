using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Trax.Api.GraphQL.Client.Trax;

/// <summary>
/// Hosted service that validates the <see cref="IGenericGraphQLClientRequest"/> types belonging
/// to one client in the registered assemblies before the app accepts traffic. Schema drift surfaces as a startup
/// failure with the specific request type and validator error, rather than a 400 on the
/// first runtime call.
///
/// Wire it up via <c>builder.UseStartupValidation(assemblies)</c> on the
/// <see cref="TraxGraphQLClientBuilder"/>. The hosted service runs once on
/// <see cref="StartAsync"/>.
/// </summary>
internal sealed class GraphQLClientStartupValidator : IHostedService
{
    private readonly IGraphQLClientValidator _validator;
    private readonly IReadOnlyList<Assembly> _assemblies;
    private readonly Func<Type, bool>? _typeFilter;
    private readonly bool _byClientKey;
    private readonly object? _serviceKey;
    private readonly Func<object, bool> _isRegisteredKey = _ => true;
    private readonly ILogger<GraphQLClientStartupValidator>? _logger;

    /// <summary>
    /// Creates the validator. <c>UseStartupValidation</c> registers it; not intended to be constructed
    /// directly.
    /// </summary>
    /// <param name="validator">The client's validator.</param>
    /// <param name="assemblies">The assemblies whose request types are validated.</param>
    /// <param name="typeFilter">Which request types to validate; <c>null</c> for all.</param>
    /// <param name="logger">Logs progress and the failing query; optional.</param>
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

    private GraphQLClientStartupValidator(
        IGraphQLClientValidator validator,
        IReadOnlyList<Assembly> assemblies,
        object? serviceKey,
        Func<object, bool> isRegisteredKey,
        ILogger<GraphQLClientStartupValidator>? logger
    )
        : this(validator, assemblies, typeFilter: null, logger)
    {
        _byClientKey = true;
        _serviceKey = serviceKey;
        _isRegisteredKey = isRegisteredKey;
    }

    /// <summary>
    /// The validator <c>UseStartupValidation</c> registers: it checks the request types that
    /// belong to the client registered under <paramref name="serviceKey"/> (marked with that key,
    /// or unmarked for <c>null</c>; see <see cref="GraphQLClientAttribute"/>) and refuses to start
    /// when there are none, or when a request in the assemblies is marked with a key
    /// <paramref name="isRegisteredKey"/> says no client is registered under.
    /// </summary>
    internal static GraphQLClientStartupValidator ForClient(
        IGraphQLClientValidator validator,
        IReadOnlyList<Assembly> assemblies,
        object? serviceKey,
        Func<object, bool> isRegisteredKey,
        ILogger<GraphQLClientStartupValidator>? logger
    ) => new(validator, assemblies, serviceKey, isRegisteredKey, logger);

    /// <summary>
    /// Validates every request type and lets a failure propagate, which stops the host from starting.
    /// A validation failure is logged with the offending query first.
    /// </summary>
    /// <param name="cancellationToken">Cancels validation.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation(
            "Validating outbound GraphQL queries across {Count} assembly(ies)",
            _assemblies.Count
        );

        try
        {
            if (_byClientKey)
                await _validator
                    .ValidateClientRequestsAsync(
                        _assemblies,
                        _serviceKey,
                        _isRegisteredKey,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            else
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

    /// <summary>Does nothing; there is nothing to stop.</summary>
    /// <param name="cancellationToken">Unused.</param>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
