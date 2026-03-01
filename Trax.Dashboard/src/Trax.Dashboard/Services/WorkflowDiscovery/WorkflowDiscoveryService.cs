using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Dashboard.Services.WorkflowDiscovery;

public class WorkflowDiscoveryService : IWorkflowDiscoveryService
{
    private readonly IServiceCollection _serviceCollection;
    private IReadOnlyList<WorkflowRegistration>? _cachedRegistrations;

    public WorkflowDiscoveryService(IServiceCollection serviceCollection)
    {
        _serviceCollection = serviceCollection;
    }

    public IReadOnlyList<WorkflowRegistration> DiscoverWorkflows()
    {
        if (_cachedRegistrations != null)
            return _cachedRegistrations;

        var serviceTrainType = typeof(IServiceTrain<,>);
        var registrations = new List<WorkflowRegistration>();

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

            registrations.Add(
                new WorkflowRegistration
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
                }
            );
        }

        // Deduplicate: each InputType maps to one workflow in the registry.
        // Prefer the interface for ServiceType and the concrete class for ImplementationType.
        _cachedRegistrations = registrations
            .GroupBy(r => r.InputType)
            .Select(g =>
            {
                var interfaceReg = g.FirstOrDefault(r => r.ServiceType.IsInterface);
                var concreteReg = g.FirstOrDefault(r => !r.ServiceType.IsInterface);
                var preferred = interfaceReg ?? concreteReg ?? g.First();

                return new WorkflowRegistration
                {
                    ServiceType = preferred.ServiceType,
                    ImplementationType =
                        concreteReg?.ImplementationType ?? preferred.ImplementationType,
                    InputType = preferred.InputType,
                    OutputType = preferred.OutputType,
                    Lifetime = preferred.Lifetime,
                    ServiceTypeName = preferred.ServiceTypeName,
                    ImplementationTypeName =
                        concreteReg?.ImplementationTypeName ?? preferred.ImplementationTypeName,
                    InputTypeName = preferred.InputTypeName,
                    OutputTypeName = preferred.OutputTypeName,
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

    private static string GetFriendlyTypeName(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name[..type.Name.IndexOf('`')];
        var args = string.Join(", ", type.GetGenericArguments().Select(GetFriendlyTypeName));
        return $"{name}<{args}>";
    }
}
