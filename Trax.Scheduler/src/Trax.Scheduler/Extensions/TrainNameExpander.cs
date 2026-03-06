using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Scheduler.Extensions;

/// <summary>
/// Expands a list of train type names to include both ServiceType.FullName and
/// ImplementationType.FullName for any matching registrations. This prevents
/// mismatches when metadata.Name is set to the interface name (via scheduler/GraphQL)
/// but the whitelist/exclusion list contains the concrete class name (or vice versa).
/// </summary>
internal static class TrainNameExpander
{
    internal static HashSet<string> ExpandTrainNames(
        IReadOnlyList<string> names,
        ITrainDiscoveryService? discoveryService
    )
    {
        var expanded = new HashSet<string>(names);

        if (discoveryService is null)
            return expanded;

        var registrations = discoveryService.DiscoverTrains();

        foreach (var name in names)
        {
            foreach (var reg in registrations)
            {
                var serviceFullName = reg.ServiceType.FullName;
                var implFullName = reg.ImplementationType.FullName;

                if (name == serviceFullName && implFullName is not null)
                    expanded.Add(implFullName);
                else if (name == implFullName && serviceFullName is not null)
                    expanded.Add(serviceFullName);
            }
        }

        return expanded;
    }
}
