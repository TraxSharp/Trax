namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Patch input for <see cref="IOperationsService.UpdateManifestGroupAsync"/>. Each field
/// is independent: any property left <c>null</c> is left untouched. Pass only the values
/// you want to change.
/// </summary>
/// <remarks>
/// To clear <c>MaxActiveJobs</c> (set it to "no per-group limit"), the dashboard sends
/// <see cref="ClearMaxActiveJobs"/> = <c>true</c>. Without that flag, a <c>null</c>
/// <see cref="MaxActiveJobs"/> means "leave as-is" since <c>int?</c> can't distinguish
/// "unset" from "set to null".
/// </remarks>
/// <param name="MaxActiveJobs">New per-group concurrency limit. Null = no change.</param>
/// <param name="ClearMaxActiveJobs">When true, sets MaxActiveJobs to null (removes the limit).</param>
/// <param name="Priority">New priority. Null = no change.</param>
/// <param name="IsEnabled">Whether the group is active. Null = no change.</param>
public record UpdateManifestGroupInput(
    int? MaxActiveJobs = null,
    bool ClearMaxActiveJobs = false,
    int? Priority = null,
    bool? IsEnabled = null
);
