using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Utils;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Metadata;
using Trax.Scheduler.Configuration;

namespace Trax.Api.Services.Lists;

/// <summary>
/// The filters of the operations list reads (<c>executions</c>, <c>deadLetters</c>,
/// <c>manifests</c>), applied in one place so a bulk operation over a filter matches exactly the
/// rows the list shows under it.
/// </summary>
internal static class ListFilters
{
    /// <summary>Whether <paramref name="filter"/> narrows the run list at all.</summary>
    public static bool Narrows(BulkExecutionFilter filter) =>
        filter.TrainState.HasValue
        || !string.IsNullOrWhiteSpace(filter.TrainName)
        || filter.StartedAfter.HasValue
        || filter.StartedBefore.HasValue
        || filter.ManifestId.HasValue
        || filter.ManifestGroupId.HasValue
        || filter.HideAdminTrains == true
        || filter.FailureClass.HasValue
        || !string.IsNullOrWhiteSpace(filter.ExternalId)
        || filter.ParentId.HasValue
        || !string.IsNullOrWhiteSpace(filter.HostName)
        || !string.IsNullOrWhiteSpace(filter.FailureJunction)
        || HasFailureText(filter);

    /// <summary>Whether <paramref name="filter"/> matches failure text, which counts differently.</summary>
    public static bool HasFailureText(BulkExecutionFilter filter) =>
        !string.IsNullOrEmpty(filter.FailureReasonContains);

    /// <summary>The runs <paramref name="filter"/> matches, in no particular order.</summary>
    public static IQueryable<Metadata> Executions(
        IDataContext db,
        IQueryable<Metadata> runs,
        BulkExecutionFilter filter
    )
    {
        if (filter.TrainState.HasValue)
            runs = runs.Where(m => m.TrainState == filter.TrainState.Value);
        if (filter.FailureClass.HasValue)
            runs = runs.Where(m => m.FailureClass == filter.FailureClass.Value);
        if (!string.IsNullOrWhiteSpace(filter.TrainName))
            runs = runs.Where(m => m.Name == filter.TrainName);
        // metadata.Name stores the interface FullName, which is what AdminTrains.FullNames holds.
        // EF translates the list Contains to a SQL IN.
        if (filter.HideAdminTrains == true)
            runs = runs.Where(m => !AdminTrains.FullNames.Contains(m.Name));
        if (filter.StartedAfter.HasValue)
            runs = runs.Where(m => m.StartTime >= filter.StartedAfter.Value);
        if (filter.StartedBefore.HasValue)
            runs = runs.Where(m => m.StartTime <= filter.StartedBefore.Value);
        if (filter.ManifestId.HasValue)
            runs = runs.Where(m => m.ManifestId == filter.ManifestId.Value);
        // Each of these three seeks an index of its own: ix_metadata_external_id,
        // ix_metadata_parent_id and ix_metadata_host_name.
        if (!string.IsNullOrWhiteSpace(filter.ExternalId))
            runs = runs.Where(m => m.ExternalId == filter.ExternalId);
        if (filter.ParentId.HasValue)
            runs = runs.Where(m => m.ParentId == filter.ParentId.Value);
        if (!string.IsNullOrWhiteSpace(filter.HostName))
            runs = runs.Where(m => m.HostName == filter.HostName);
        // ix_metadata_failure_junction holds only the runs that failed, in id order under each
        // junction, so a page of one junction's failures reads just that page.
        if (!string.IsNullOrWhiteSpace(filter.FailureJunction))
            runs = runs.Where(m => m.FailureJunction == filter.FailureJunction);
        // Lowered, with the term's wildcards escaped: the expression
        // ix_metadata_failure_reason_trgm is built over, so a rare term reads only its matches.
        if (HasFailureText(filter))
        {
            var pattern = LikePattern.Contains(filter.FailureReasonContains!);
            runs = runs.Where(m =>
                EF.Functions.Like(m.FailureReason!.ToLower(), pattern, LikePattern.Escape)
            );
        }
        if (filter.ManifestGroupId.HasValue)
        {
            // Runs of a group are the runs of any manifest in it. The subquery stays
            // index-friendly: manifest.manifest_group_id is indexed, and the resulting manifest
            // ids seek ix_metadata_manifest_state on the metadata side.
            var groupManifestIds = db
                .Manifests.AsNoTracking()
                .Where(mf => mf.ManifestGroupId == filter.ManifestGroupId.Value)
                .Select(mf => (long?)mf.Id);
            runs = runs.Where(m => groupManifestIds.Contains(m.ManifestId));
        }

        return runs;
    }

    /// <summary>The dead letters <paramref name="filter"/> matches.</summary>
    public static IQueryable<DeadLetter> DeadLetters(
        IQueryable<DeadLetter> deadLetters,
        BulkDeadLetterFilter filter
    )
    {
        if (filter.Status.HasValue)
            deadLetters = deadLetters.Where(dl => dl.Status == filter.Status.Value);
        if (filter.ManifestId.HasValue)
            deadLetters = deadLetters.Where(dl => dl.ManifestId == filter.ManifestId.Value);
        return deadLetters;
    }

    /// <summary>Whether <paramref name="filter"/> narrows the manifest list at all.</summary>
    public static bool Narrows(BulkManifestFilter filter) =>
        filter.IsEnabled.HasValue
        || filter.ScheduleType.HasValue
        || !string.IsNullOrWhiteSpace(filter.NameContains)
        || filter.ManifestGroupId.HasValue
        || filter.HideAdminTrains == true;

    /// <summary>The manifests <paramref name="filter"/> matches.</summary>
    public static IQueryable<Manifest> Manifests(
        IQueryable<Manifest> manifests,
        BulkManifestFilter filter
    )
    {
        if (filter.IsEnabled.HasValue)
            manifests = manifests.Where(m => m.IsEnabled == filter.IsEnabled.Value);
        if (filter.ScheduleType.HasValue)
            manifests = manifests.Where(m => m.ScheduleType == filter.ScheduleType.Value);
        if (!string.IsNullOrWhiteSpace(filter.NameContains))
            manifests = manifests.Where(m => m.Name.Contains(filter.NameContains));
        if (filter.ManifestGroupId.HasValue)
            manifests = manifests.Where(m => m.ManifestGroupId == filter.ManifestGroupId.Value);
        // A manifest's Name is its train's interface FullName, the form AdminTrains.FullNames holds.
        if (filter.HideAdminTrains == true)
            manifests = manifests.Where(m => !AdminTrains.FullNames.Contains(m.Name));
        return manifests;
    }
}
