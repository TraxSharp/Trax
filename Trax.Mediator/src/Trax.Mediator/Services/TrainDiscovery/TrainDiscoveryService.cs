using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrainRegistry;

namespace Trax.Mediator.Services.TrainDiscovery;

/// <summary>
/// Default <see cref="ITrainDiscoveryService"/>: reads train registrations from an
/// <see cref="IServiceCollection"/>, so it works before the container is built. Registered as a
/// singleton by <c>AddMediator</c>; Trax.Scheduler and Trax.Api also construct it during
/// registration. Infrastructure; resolve <see cref="ITrainDiscoveryService"/> instead.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class TrainDiscoveryService : ITrainDiscoveryService
{
    private readonly IServiceCollection _serviceCollection;
    private IReadOnlyList<TrainRegistration>? _cachedRegistrations;

    /// <summary>Creates a discovery service over <paramref name="serviceCollection"/>.</summary>
    /// <param name="serviceCollection">
    /// The collection to scan. It is read on the first <see cref="DiscoverTrains"/> call, not
    /// here, and the result is cached: trains registered after that first call are not seen.
    /// </param>
    public TrainDiscoveryService(IServiceCollection serviceCollection)
    {
        _serviceCollection = serviceCollection;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// One registration is returned per train, and two trains that take the same input type are
    /// both listed. A train registered under both its interface and its concrete class, as
    /// <c>AddScopedTraxRoute</c> does, is listed once, with the interface as
    /// <see cref="TrainRegistration.ServiceType"/> and the class as
    /// <see cref="TrainRegistration.ImplementationType"/>. The two are only paired when resolving
    /// that interface yields that class, so a registration's requirements and attributes are always
    /// those of the train its service type resolves. Not synchronized: two threads making the first
    /// call at once both scan, and either result is kept.
    /// </remarks>
    /// <exception cref="TrainException">
    /// Two different classes are registered under one class service type, such as a shared base
    /// class. Both would be listed under the same name while the container resolves only the last,
    /// so the train a name describes would not be the one that runs.
    /// </exception>
    public IReadOnlyList<TrainRegistration> DiscoverTrains()
    {
        if (_cachedRegistrations != null)
            return _cachedRegistrations;

        // Classes registered as themselves (the first registration of each), and the last
        // descriptor of each train interface, which is the one DI resolves.
        var classes = new List<RegisteredClass>();
        var seenClasses = new HashSet<(Type, Type)>();
        var interfaces = new Dictionary<Type, (int Index, ServiceDescriptor Descriptor)>();

        for (var index = 0; index < _serviceCollection.Count; index++)
        {
            var descriptor = _serviceCollection[index];
            var serviceType = descriptor.ServiceType;

            if (TrainServiceType.FindClosedServiceTrain(serviceType) is null)
                continue;

            if (serviceType.IsInterface)
            {
                var firstIndex = interfaces.TryGetValue(serviceType, out var earlier)
                    ? earlier.Index
                    : index;
                interfaces[serviceType] = (firstIndex, descriptor);
                continue;
            }

            // A class registered through a factory cannot be read without running the factory.
            if (KnownImplementation(descriptor) is not { } implementation)
                continue;

            if (seenClasses.Add((serviceType, implementation)))
                classes.Add(
                    new RegisteredClass(index, serviceType, implementation, descriptor.Lifetime)
                );
        }

        RefuseSharedClassServiceTypes(classes);

        var registrations = new List<(int Index, TrainRegistration Registration)>();
        var pairedInterfaces = new HashSet<Type>();

        foreach (var registered in classes)
        {
            var own =
                registered.ServiceType == registered.Implementation
                    ? OwnInterface(registered.Implementation)
                    : null;

            if (
                own is not null
                && interfaces.TryGetValue(own, out var entry)
                && ResolvesTo(entry.Descriptor, own, registered.Implementation, classes)
            )
            {
                pairedInterfaces.Add(own);
                registrations.Add(
                    (
                        registered.Index,
                        Build(own, registered.Implementation, entry.Descriptor.Lifetime)
                    )
                );
                continue;
            }

            registrations.Add(
                (
                    registered.Index,
                    Build(registered.ServiceType, registered.Implementation, registered.Lifetime)
                )
            );
        }

        foreach (var (serviceType, (index, descriptor)) in interfaces)
        {
            if (pairedInterfaces.Contains(serviceType))
                continue;

            var implementation = KnownImplementation(descriptor);

            if (implementation is null)
            {
                // A factory whose class cannot be read. When a registered class implements the
                // interface, that class is listed under its own entry already, and listing this
                // interface could name a different train from the one whose attributes were read.
                if (classes.Any(c => serviceType.IsAssignableFrom(c.Implementation)))
                    continue;

                implementation = serviceType;
            }

            registrations.Add((index, Build(serviceType, implementation, descriptor.Lifetime)));
        }

        _cachedRegistrations = registrations
            .OrderBy(r => r.Index)
            .Select(r => r.Registration)
            .ToList()
            .AsReadOnly();

        return _cachedRegistrations;
    }

    /// <summary>
    /// Refuses two different classes registered under one class service type. A train is found
    /// and authorized by its service type's name, and the container resolves the last
    /// registration of a type, so with two of them the attributes read for one train would gate
    /// a run of the other.
    /// </summary>
    private static void RefuseSharedClassServiceTypes(IReadOnlyList<RegisteredClass> classes)
    {
        var shared = classes
            .GroupBy(c => c.ServiceType)
            .Where(g => g.Select(c => c.Implementation).Distinct().Count() > 1)
            .ToList();

        if (shared.Count == 0)
            return;

        throw new TrainException(
            string.Join(
                " ",
                shared.Select(g =>
                    $"{g.Count()} trains are registered under {g.Key.FullName}: "
                    + string.Join(", ", g.Select(c => c.Implementation.FullName))
                    + "."
                )
            )
                + " A train is found by the name of the type it is registered under, and the "
                + "container runs only the last registration, so the train that name describes "
                + "would not be the train that runs. Register each train under its own interface "
                + "or class."
        );
    }

    private sealed record RegisteredClass(
        int Index,
        Type ServiceType,
        Type Implementation,
        ServiceLifetime Lifetime
    );

    /// <summary>The class a descriptor resolves to without running a factory, or null.</summary>
    private static Type? KnownImplementation(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();

    /// <summary>
    /// The interface scanning registers <paramref name="implementation"/> under, or null when that
    /// is not a single train interface.
    /// </summary>
    private static Type? OwnInterface(Type implementation)
    {
        // Select returns an interface or throws: the train's own interface, or the closed
        // IServiceTrain<,> when it has none.
        try
        {
            return TrainServiceType.Select(implementation);
        }
        catch (TrainException)
        {
            // Discovery is permissive; scanning is where an ambiguous train is refused.
            return null;
        }
    }

    /// <summary>
    /// Whether resolving <paramref name="descriptor"/> yields <paramref name="implementation"/>.
    /// A typed descriptor says so itself. A factory is taken to when no other registered class
    /// implements the interface, which is the shape <c>AddScopedTraxRoute</c> leaves.
    /// </summary>
    private static bool ResolvesTo(
        ServiceDescriptor descriptor,
        Type serviceType,
        Type implementation,
        IReadOnlyList<RegisteredClass> classes
    )
    {
        var known = KnownImplementation(descriptor);
        if (known is not null)
            return known == implementation;

        return classes
            .Select(c => c.Implementation)
            .Where(serviceType.IsAssignableFrom)
            .All(c => c == implementation);
    }

    private static TrainRegistration Build(
        Type serviceType,
        Type implementationType,
        ServiceLifetime lifetime
    )
    {
        var genericArgs = TrainServiceType
            .FindClosedServiceTrain(serviceType)!
            .GetGenericArguments();
        var inputType = genericArgs[0];
        var outputType = genericArgs[1];

        var (hasAuthorize, policies, roles, roleSets) = GetAuthorizationRequirements(
            implementationType
        );
        var graphql = GetGraphQLMetadata(implementationType);

        return new TrainRegistration
        {
            ServiceType = serviceType,
            ImplementationType = implementationType,
            InputType = inputType,
            OutputType = outputType,
            Lifetime = lifetime,
            ServiceTypeName = GetFriendlyTypeName(serviceType),
            ImplementationTypeName = GetFriendlyTypeName(implementationType),
            InputTypeName = GetFriendlyTypeName(inputType),
            OutputTypeName = GetFriendlyTypeName(outputType),
            RequiredPolicies = policies,
            RequiredRoles = roles,
            RequiredRoleSets = roleSets,
            HasAuthorizeAttribute = hasAuthorize,
            HasAllowAnonymousAttribute = HasAllowAnonymousAttribute(implementationType),
            IsQuery = graphql.IsQuery,
            IsMutation = graphql.IsMutation,
            IsBroadcastEnabled = HasBroadcastAttribute(implementationType),
            IsRemote = HasRemoteAttribute(implementationType),
            GraphQLName = graphql.Name,
            GraphQLDescription = graphql.Description,
            GraphQLDeprecationReason = graphql.DeprecationReason,
            GraphQLOperations = graphql.Operations,
            GraphQLNamespace = graphql.Namespace,
            MaxConcurrentRun = GetConcurrencyLimit(implementationType),
            HasQueueSubjectKey =
                QueueMemberOverrides.QueueSubjectKey(implementationType) is not null,
        };
    }

    private static (
        bool HasAuthorize,
        IReadOnlyList<string> Policies,
        IReadOnlyList<string> Roles,
        IReadOnlyList<IReadOnlyList<string>> RoleSets
    ) GetAuthorizationRequirements(Type implementationType)
    {
        // Attributes declared on an interface do not flow to implementing types via
        // GetCustomAttributes(inherit: true). Union attributes from every surface a
        // developer might reasonably decorate: the implementation (including its base
        // chain, via inherit: true) and the interfaces it implements. Ensures
        // [TraxAuthorize] on IFooTrain or an abstract base class is honored.
        var carriers = new List<Type> { implementationType };
        carriers.AddRange(implementationType.GetInterfaces());

        var attributes = carriers
            .SelectMany(t => t.GetCustomAttributes<TraxAuthorizeAttribute>(inherit: true))
            .Distinct()
            .ToList();

        if (attributes.Count == 0)
            return (
                false,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<IReadOnlyList<string>>()
            );

        // Discovery is permissive: it does not throw on malformed attribute shapes
        // (empty policy strings, whitespace-only Roles). Malformed shapes silently
        // produce empty collections here. Startup-time validation in
        // AuthorizationRegistrationValidator surfaces those cases loudly so hosts
        // can fix them before serving traffic, while keeping discovery itself safe
        // to call from assembly scanners that may pick up intentionally-malformed
        // fixtures in test projects.
        var policies = new List<string>();
        foreach (var attr in attributes.Where(a => !string.IsNullOrWhiteSpace(a.Policy)))
        {
            var policy = attr.Policy!;
            if (!policies.Contains(policy))
                policies.Add(policy);
        }

        var roles = new List<string>();
        var roleSets = new List<IReadOnlyList<string>>();
        foreach (var attr in attributes.Where(a => a.Roles is not null))
        {
            // Kept exactly as declared. A role is matched against the principal's role claims
            // ordinally, the way @authorize matches them, so "Admin" and "admin" are different
            // roles. Folding case here once let a claim satisfy a role it only resembled: the
            // invariant culture upper-cases the long s to S.
            var parsed = attr.Roles!.Split(',', StringSplitOptions.TrimEntries)
                .Where(r => r.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var role in parsed)
            {
                if (!roles.Contains(role, StringComparer.Ordinal))
                    roles.Add(role);
            }

            // One set per attribute, because separate attributes combine with AND. A set another
            // attribute already declared adds nothing to the requirement.
            if (
                parsed.Count > 0
                && !roleSets.Any(set =>
                    set.Count == parsed.Count
                    && set.All(r => parsed.Contains(r, StringComparer.Ordinal))
                )
            )
                roleSets.Add(parsed.AsReadOnly());
        }

        return (true, policies.AsReadOnly(), roles.AsReadOnly(), roleSets.AsReadOnly());
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

    private static bool HasAllowAnonymousAttribute(Type implementationType)
    {
        // Match the carrier-union walk in GetAuthorizationRequirements: an interface
        // attribute does not flow to the implementing type via inherit: true, so check
        // the implementation (plus its base chain) and every interface it implements.
        if (
            implementationType.GetCustomAttribute<TraxAllowAnonymousAttribute>(inherit: true)
            is not null
        )
            return true;

        return implementationType
            .GetInterfaces()
            .Any(i => i.GetCustomAttribute<TraxAllowAnonymousAttribute>(inherit: true) is not null);
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
