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

        foreach (var twins in ExpandEach(names, discoveryService).Values)
            expanded.UnionWith(twins);

        return expanded;
    }

    /// <summary>
    /// The same expansion, but keyed by the declared name so a caller can carry per-name settings
    /// through it. Every declared name maps to a set containing itself plus any twin found.
    /// </summary>
    internal static Dictionary<string, HashSet<string>> ExpandEach(
        IReadOnlyList<string> names,
        ITrainDiscoveryService? discoveryService
    )
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var name in names)
            result[name] = [name];

        if (discoveryService is null)
            return result;

        var registrations = discoveryService.DiscoverTrains();

        foreach (var name in names)
        {
            foreach (var reg in registrations)
            {
                var serviceFullName = reg.ServiceType.FullName;
                var implFullName = reg.ImplementationType.FullName;

                if (name == serviceFullName && implFullName is not null)
                    result[name].Add(implFullName);
                else if (name == implFullName && serviceFullName is not null)
                    result[name].Add(serviceFullName);
            }
        }

        return result;
    }
}
