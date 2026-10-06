using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Extensions;

/// <summary>
/// Turns the declared cleanup whitelist into the set of sweeps the cleanup actually runs: every
/// train name the sweep should match, against the cutoff it should be matched at.
/// </summary>
/// <remarks>
/// Built in one place because two callers need the same answer: the cleanup junction, which runs
/// it every cycle, and the startup validator, which runs it once to refuse a configuration that
/// cannot mean what it says.
/// </remarks>
internal static class MetadataRetentionPlan
{
    /// <summary>
    /// A declared name and a name it expanded to, asking for different retentions.
    /// </summary>
    /// <remarks>
    /// Only reachable through expansion. Declaring the same literal name twice with different
    /// retentions is refused by <see cref="MetadataCleanupConfiguration.AddTrainType(string, TimeSpan)"/>,
    /// but an interface name and its class name are two unrelated strings until
    /// <see cref="ITrainDiscoveryService"/> relates them, which only happens at run time.
    /// </remarks>
    internal readonly record struct RetentionConflict(
        string Name,
        string DeclaredA,
        TimeSpan RetentionA,
        string DeclaredB,
        TimeSpan RetentionB
    )
    {
        public override string ToString() =>
            $"'{Name}' is covered by '{DeclaredA}' ({RetentionA}) and by '{DeclaredB}' "
            + $"({RetentionB}), which are the same train under its interface and class names.";
    }

    /// <summary>
    /// Every train name the cleanup should match, against the retention it should be matched at.
    /// </summary>
    /// <param name="configuration">The cleanup configuration, read for its whitelist and default.</param>
    /// <param name="discoveryService">Used to pair interface names with class names. Optional.</param>
    /// <param name="conflicts">
    /// Names two declarations disagree about. The longest retention wins in the returned plan, so
    /// the sweep never deletes earlier than the longest anyone asked for, and the caller decides
    /// whether to warn or refuse.
    /// </param>
    internal static Dictionary<string, TimeSpan> Build(
        MetadataCleanupConfiguration configuration,
        ITrainDiscoveryService? discoveryService,
        out List<RetentionConflict> conflicts
    )
    {
        conflicts = [];

        var plan = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);
        var declaredBy = new Dictionary<string, string>(StringComparer.Ordinal);

        var expansions = TrainNameExpander.ExpandEach(
            configuration.TrainTypeWhitelist,
            discoveryService
        );

        foreach (var (declared, names) in expansions)
        {
            var retention =
                configuration.TrainTypeRetentions.GetValueOrDefault(declared)
                ?? configuration.RetentionPeriod;

            foreach (var name in names)
            {
                if (!plan.TryGetValue(name, out var existing))
                {
                    plan[name] = retention;
                    declaredBy[name] = declared;
                    continue;
                }

                if (existing == retention)
                    continue;

                conflicts.Add(
                    new RetentionConflict(name, declaredBy[name], existing, declared, retention)
                );

                // Longest wins. A name that lands in two groups is swept by whichever group runs
                // first, so taking the shorter value would silently delete rows a consumer asked
                // to keep. Keeping too long is recoverable; deleting early is not.
                if (retention > existing)
                {
                    plan[name] = retention;
                    declaredBy[name] = declared;
                }
            }
        }

        // Internal scheduler trains are swept unconditionally and always at the default. A
        // per-train retention for one is refused at configuration time, so this only ever fills
        // in names nobody listed.
        foreach (var adminName in AdminTrains.FullNames)
            plan[adminName] = configuration.RetentionPeriod;

        return plan;
    }

    /// <summary>
    /// The plan as the sweeps it implies: one cutoff, and the names swept at it.
    /// </summary>
    internal static IEnumerable<(TimeSpan Retention, List<string> Names)> GroupByRetention(
        Dictionary<string, TimeSpan> plan
    ) => plan.GroupBy(entry => entry.Value).Select(g => (g.Key, g.Select(e => e.Key).ToList()));
}
