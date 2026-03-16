using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Services.TrainDiscovery;

/// <inheritdoc />
public class TrainDiscoveryService : ITrainDiscoveryService
{
    private readonly IServiceCollection _serviceCollection;
    private IReadOnlyList<TrainRegistration>? _cachedRegistrations;

    public TrainDiscoveryService(IServiceCollection serviceCollection)
    {
        _serviceCollection = serviceCollection;
    }

    public IReadOnlyList<TrainRegistration> DiscoverTrains()
    {
        if (_cachedRegistrations != null)
            return _cachedRegistrations;

        var serviceTrainType = typeof(IServiceTrain<,>);
        var registrations = new List<TrainRegistration>();

        foreach (var descriptor in _serviceCollection)
        {
            var serviceType = descriptor.ServiceType;

            // Find IServiceTrain<,> either directly or via interface hierarchy
            Type? effectInterface = FindServiceTrainInterface(serviceType, serviceTrainType);

            if (effectInterface == null)
                continue;

            // Skip concrete type registrations from the dual-registration pattern
            // (AddScopedTraxRoute registers both TImplementation and TService)
            if (descriptor.ImplementationFactory != null && !serviceType.IsInterface)
                continue;

            var genericArgs = effectInterface.GetGenericArguments();
            var inputType = genericArgs[0];
            var outputType = genericArgs[1];

            var implementationType =
                descriptor.ImplementationType
                ?? descriptor.ImplementationInstance?.GetType()
                ?? descriptor.ServiceType;

            var (policies, roles) = GetAuthorizationRequirements(implementationType);
            var graphql = GetGraphQLMetadata(implementationType);
            var broadcastEnabled = HasBroadcastAttribute(implementationType);
            var isRemote = HasRemoteAttribute(implementationType);
            var concurrencyLimit = GetConcurrencyLimit(implementationType);

            registrations.Add(
                new TrainRegistration
                {
                    ServiceType = serviceType,
                    ImplementationType = implementationType,
                    InputType = inputType,
                    OutputType = outputType,
                    Lifetime = descriptor.Lifetime,
                    ServiceTypeName = GetFriendlyTypeName(serviceType),
                    ImplementationTypeName = GetFriendlyTypeName(implementationType),
                    InputTypeName = GetFriendlyTypeName(inputType),
                    OutputTypeName = GetFriendlyTypeName(outputType),
                    RequiredPolicies = policies,
                    RequiredRoles = roles,
                    IsQuery = graphql.IsQuery,
                    IsMutation = graphql.IsMutation,
                    IsBroadcastEnabled = broadcastEnabled,
                    IsRemote = isRemote,
                    GraphQLName = graphql.Name,
                    GraphQLDescription = graphql.Description,
                    GraphQLDeprecationReason = graphql.DeprecationReason,
                    GraphQLOperations = graphql.Operations,
                    GraphQLNamespace = graphql.Namespace,
                    MaxConcurrentRun = concurrencyLimit,
                }
            );
        }

        // Deduplicate: each InputType maps to one train in the registry.
        // Prefer the interface for ServiceType and the concrete class for ImplementationType.
        _cachedRegistrations = registrations
            .GroupBy(r => r.InputType)
            .Select(g =>
            {
                var interfaceReg = g.FirstOrDefault(r => r.ServiceType.IsInterface);
                var concreteReg = g.FirstOrDefault(r => !r.ServiceType.IsInterface);
                var preferred = interfaceReg ?? concreteReg ?? g.First();

                var implType = concreteReg?.ImplementationType ?? preferred.ImplementationType;
                var (policies, roles) = GetAuthorizationRequirements(implType);
                var graphql = GetGraphQLMetadata(implType);
                var broadcastEnabled = HasBroadcastAttribute(implType);
                var isRemote = HasRemoteAttribute(implType);
                var concurrencyLimit = GetConcurrencyLimit(implType);

                return new TrainRegistration
                {
                    ServiceType = preferred.ServiceType,
                    ImplementationType = implType,
                    InputType = preferred.InputType,
                    OutputType = preferred.OutputType,
                    Lifetime = preferred.Lifetime,
                    ServiceTypeName = preferred.ServiceTypeName,
                    ImplementationTypeName =
                        concreteReg?.ImplementationTypeName ?? preferred.ImplementationTypeName,
                    InputTypeName = preferred.InputTypeName,
                    OutputTypeName = preferred.OutputTypeName,
                    RequiredPolicies = policies,
                    RequiredRoles = roles,
                    IsQuery = graphql.IsQuery,
                    IsMutation = graphql.IsMutation,
                    IsBroadcastEnabled = broadcastEnabled,
                    IsRemote = isRemote,
                    GraphQLName = graphql.Name,
                    GraphQLDescription = graphql.Description,
                    GraphQLDeprecationReason = graphql.DeprecationReason,
                    GraphQLOperations = graphql.Operations,
                    GraphQLNamespace = graphql.Namespace,
                    MaxConcurrentRun = concurrencyLimit,
                };
            })
            .ToList()
            .AsReadOnly();

        return _cachedRegistrations;
    }

    private static Type? FindServiceTrainInterface(Type type, Type serviceTrainType)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == serviceTrainType)
            return type;

        return type.GetInterfaces()
            .FirstOrDefault(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == serviceTrainType
            );
    }

    private static (
        IReadOnlyList<string> Policies,
        IReadOnlyList<string> Roles
    ) GetAuthorizationRequirements(Type implementationType)
    {
        var attributes = implementationType.GetCustomAttributes<TraxAuthorizeAttribute>().ToList();

        if (attributes.Count == 0)
            return (Array.Empty<string>(), Array.Empty<string>());

        var policies = attributes
            .Where(a => a.Policy is not null)
            .Select(a => a.Policy!)
            .Distinct()
            .ToList();

        var roles = attributes
            .Where(a => a.Roles is not null)
            .SelectMany(a => a.Roles!.Split(',', StringSplitOptions.TrimEntries))
            .Where(r => r.Length > 0)
            .Distinct()
            .ToList();

        return (policies.AsReadOnly(), roles.AsReadOnly());
    }

    private static (
        bool IsQuery,
        bool IsMutation,
        string? Name,
        string? Description,
        string? DeprecationReason,
        GraphQLOperation Operations,
        string? Namespace
    ) GetGraphQLMetadata(Type implementationType)
    {
        var queryAttr = implementationType.GetCustomAttribute<TraxQueryAttribute>();
        if (queryAttr is not null)
        {
            return (
                true,
                false,
                queryAttr.Name,
                queryAttr.Description,
                queryAttr.DeprecationReason,
                GraphQLOperation.Run,
                queryAttr.Namespace
            );
        }

        var mutationAttr = implementationType.GetCustomAttribute<TraxMutationAttribute>();
        if (mutationAttr is not null)
        {
            return (
                false,
                true,
                mutationAttr.Name,
                mutationAttr.Description,
                mutationAttr.DeprecationReason,
                mutationAttr.Operations,
                mutationAttr.Namespace
            );
        }

        return (false, false, null, null, null, GraphQLOperation.Run, null);
    }

    private static bool HasBroadcastAttribute(Type implementationType) =>
        implementationType.GetCustomAttribute<TraxBroadcastAttribute>() is not null;

    private static bool HasRemoteAttribute(Type implementationType) =>
        implementationType.GetCustomAttribute<TraxRemoteAttribute>() is not null;

    private static int? GetConcurrencyLimit(Type implementationType) =>
        implementationType.GetCustomAttribute<TraxConcurrencyLimitAttribute>()?.MaxConcurrent;

    private static string GetFriendlyTypeName(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name[..type.Name.IndexOf('`')];
        var args = string.Join(", ", type.GetGenericArguments().Select(GetFriendlyTypeName));
        return $"{name}<{args}>";
    }
}
