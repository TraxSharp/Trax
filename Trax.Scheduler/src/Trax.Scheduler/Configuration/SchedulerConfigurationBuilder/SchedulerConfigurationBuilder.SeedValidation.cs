namespace Trax.Scheduler.Configuration;

public partial class SchedulerConfigurationBuilder
{
    // What each schedule or batch declares about its group and its prune, checked at build time.
    private readonly List<GroupDeclaration> _groupDeclarations = [];
    private readonly List<BatchPruneDeclaration> _batchPrunes = [];
    private readonly List<SingleScheduleDeclaration> _singleSchedules = [];

    private sealed record GroupDeclaration(
        string Group,
        string DeclaredBy,
        int? Priority,
        bool MaxActiveJobsStated,
        int? MaxActiveJobs,
        bool? IsEnabled
    );

    private sealed record BatchPruneDeclaration(
        string Prefix,
        string DeclaredBy,
        string Group,
        bool ScopedToGroup
    );

    private sealed record SingleScheduleDeclaration(string ExternalId, string Group);

    /// <summary>
    /// Records the group settings a single manifest's options state, and its external ID and
    /// group, for <see cref="ValidateSeedDeclarations"/>.
    /// </summary>
    private void DeclareGroup(string externalId, ScheduleOptions resolved)
    {
        var group = resolved._groupId ?? externalId;
        AddGroupDeclaration(
            group,
            $"'{externalId}'",
            resolved,
            ownsGroup: resolved._groupId is null
        );
        _singleSchedules.Add(new SingleScheduleDeclaration(externalId, group));
    }

    /// <summary>
    /// Records a batch's group settings and prune, for <see cref="ValidateSeedDeclarations"/>.
    /// The group is the one the scheduler seeds the batch into: its group name, else its prune
    /// prefix, else its first external ID.
    /// </summary>
    private void DeclareBatchGroup(string firstId, ScheduleOptions resolved)
    {
        var declaredBy = resolved._batchName is { } name
            ? $"batch '{name}'"
            : $"the batch starting '{firstId}'";
        var group = resolved._groupId ?? resolved._prunePrefix ?? firstId;
        var ownsGroup =
            resolved._groupId is null
            || (resolved._batchName is not null && resolved._groupId == resolved._batchName);

        AddGroupDeclaration(group, declaredBy, resolved, ownsGroup);

        if (resolved._prunePrefix is { } prefix)
            _batchPrunes.Add(
                new BatchPruneDeclaration(
                    prefix,
                    declaredBy,
                    group,
                    ScopedToGroup: resolved._batchName is not null
                )
            );
    }

    private void AddGroupDeclaration(
        string group,
        string declaredBy,
        ScheduleOptions resolved,
        bool ownsGroup
    )
    {
        var options = resolved._groupOptions;
        _groupDeclarations.Add(
            new GroupDeclaration(
                group,
                declaredBy,
                options?._priority ?? (ownsGroup ? resolved._priority : null),
                options?._maxActiveJobsStated ?? false,
                options?._maxActiveJobs,
                options?._isEnabled
            )
        );
    }

    /// <summary>
    /// Fails the build when two members of one manifest group state different values for the same
    /// group setting, or when a batch's prune could delete another batch's manifests or a manifest
    /// scheduled on its own.
    /// </summary>
    /// <remarks>
    /// A group setting is written whenever a member's options state it, so two members stating
    /// different values would overwrite each other at every start, and which one held would depend
    /// on seeding order. A batch's prune deletes the manifests whose external ID starts with its
    /// prefix and that the batch no longer declares; a named batch prunes only within its own
    /// group, so overlapping prefixes are refused only where the groups do not tell them apart. A
    /// single schedule the prune reaches is never among the batch's own manifests, so the prune
    /// would delete it, with its history, and its seed would recreate it, at every start.
    /// </remarks>
    private void ValidateSeedDeclarations()
    {
        foreach (var group in _groupDeclarations.GroupBy(d => d.Group))
        {
            var members = group.ToList();
            RequireAgreement(
                group.Key,
                "Priority",
                members.Where(m => m.Priority is not null),
                m => m.Priority
            );
            RequireAgreement(
                group.Key,
                "MaxActiveJobs",
                members.Where(m => m.MaxActiveJobsStated),
                m => m.MaxActiveJobs
            );
            RequireAgreement(
                group.Key,
                "Enabled",
                members.Where(m => m.IsEnabled is not null),
                m => m.IsEnabled
            );
        }

        for (var i = 0; i < _batchPrunes.Count; i++)
        for (var j = 0; j < _batchPrunes.Count; j++)
        {
            if (i == j)
                continue;

            var shorter = _batchPrunes[i];
            var longer = _batchPrunes[j];
            if (!longer.Prefix.StartsWith(shorter.Prefix, StringComparison.Ordinal))
                continue;

            // A group-scoped prune never reaches outside its group.
            if (shorter.ScopedToGroup && shorter.Group != longer.Group)
                continue;

            throw new InvalidOperationException(
                $"{Capitalise(shorter.DeclaredBy)} prunes manifests whose external ID starts with "
                    + $"'{shorter.Prefix}', which includes the manifests of {longer.DeclaredBy} "
                    + $"(prefix '{longer.Prefix}'). Each start, one batch would delete the other's "
                    + "manifests and their history.\n\n"
                    + "Give the batches names where neither plus '-' starts the other, or put them "
                    + "in different groups."
            );
        }

        RejectSingleSchedulesInsideBatchPrunes();
    }

    private void RejectSingleSchedulesInsideBatchPrunes()
    {
        foreach (var prune in _batchPrunes)
        foreach (var single in _singleSchedules)
        {
            if (!single.ExternalId.StartsWith(prune.Prefix, StringComparison.Ordinal))
                continue;

            // A group-scoped prune never reaches outside its group.
            if (prune.ScopedToGroup && prune.Group != single.Group)
                continue;

            throw new InvalidOperationException(
                $"{Capitalise(prune.DeclaredBy)} prunes manifests whose external ID starts with "
                    + $"'{prune.Prefix}', which includes '{single.ExternalId}', scheduled on its own. "
                    + "Each start, the batch would delete it and its history, and its own schedule "
                    + "would create it again.\n\n"
                    + $"Give '{single.ExternalId}' an external ID that does not start with "
                    + $"'{prune.Prefix}'"
                    + (prune.ScopedToGroup ? ", put it in a different group," : "")
                    + " or make it one of the batch's items."
            );
        }
    }

    private static void RequireAgreement<T>(
        string group,
        string setting,
        IEnumerable<GroupDeclaration> stating,
        Func<GroupDeclaration, T> value
    )
    {
        var list = stating.ToList();
        if (list.Count < 2)
            return;

        var first = list[0];
        var conflicting = list.FirstOrDefault(m => !Equals(value(m), value(first)));
        if (conflicting is null)
            return;

        throw new InvalidOperationException(
            $"Manifest group '{group}' is given two different {setting} values: "
                + $"{Describe(value(first))} by {first.DeclaredBy} and "
                + $"{Describe(value(conflicting))} by {conflicting.DeclaredBy}. "
                + "Every start writes a group setting a member states, so the value in force "
                + "would depend on seeding order.\n\n"
                + $"State {setting} on one member of the group, or the same value on each."
        );
    }

    private static string Describe<T>(T value) => value is null ? "none" : value.ToString()!;

    // Only ever given a batch's description ("batch '…'", "the batch starting '…'"), never empty.
    private static string Capitalise(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}
