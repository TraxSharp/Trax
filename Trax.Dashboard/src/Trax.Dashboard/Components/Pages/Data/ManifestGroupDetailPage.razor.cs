using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Shared;
using Trax.Dashboard.Models;
using Trax.Dashboard.Utilities;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Dashboard.Utilities.DashboardFormatters;

namespace Trax.Dashboard.Components.Pages.Data;

/// <summary>
/// The page for one manifest group, at <c>/trax/data/manifest-groups/{id}</c>: its settings
/// (max active jobs, priority, enabled), run counts, its manifests and runs, and the groups it
/// depends on or that depend on it. The user can edit and save the settings, trigger every
/// manifest in the group, and cancel all of its running trains. Part of the dashboard UI, routed by the package; not intended to be used directly.
/// </summary>
public partial class ManifestGroupDetailPage
{
    [Inject]
    private IDataContextProviderFactory DataContextFactory { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Inject]
    private ITraxScheduler TraxScheduler { get; set; } = default!;

    [Inject]
    private IOperationsService OperationsService { get; set; } = default!;

    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = default!;

    /// <summary>The manifest group's database id, from the route.</summary>
    [Parameter]
    public long ManifestGroupId { get; set; }

    /// <inheritdoc/>
    /// <remarks>Returns <see cref="ManifestGroupId"/>.</remarks>
    private protected override object? GetRouteKey() => ManifestGroupId;

    private ManifestGroup? _group;
    private DagLayout? _dagLayout;
    private bool _triggering;
    private string? _triggerError;
    private bool _cancellingAll;

    // ── Summary counts, from IOperationsService.GetManifestGroupExecutionStatsAsync ──
    private long _manifestCount;
    private long _completedCount;
    private long _failedCount;
    private long _inProgressCount;

    // ── Grid references for server-side reload ──
    private TraxDataGrid<ManifestRow>? _manifestsGrid;
    private TraxDataGrid<RunRow>? _executionsGrid;
    private readonly GridCount _manifestsCount = new();
    private readonly GridCount _executionsCount = new();

    // ── Settings form ──
    // The form edits a copy, never _group, so a poll can refresh _group without touching unsaved
    // edits. _savedSettings is what the form was last loaded from or saved as; a save sends only
    // the fields that differ from it.
    private GroupSettings? _settings;
    private GroupSettings? _savedSettings;

    private bool IsSettingsDirty =>
        _settings is not null && _savedSettings is not null && _settings != _savedSettings;

    /// <summary>
    /// The editable settings of one group. A record so that "dirty" is value inequality with the
    /// snapshot, and the group id is part of it so edits never carry over to another group.
    /// </summary>
    private sealed record GroupSettings
    {
        public required long GroupId { get; init; }
        public int? MaxActiveJobs { get; set; }
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }

        public static GroupSettings From(ManifestGroup group) =>
            new()
            {
                GroupId = group.Id,
                MaxActiveJobs = group.MaxActiveJobs,
                Priority = group.Priority,
                IsEnabled = group.IsEnabled,
            };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Drops the previous group, its counts and any unsaved edits, so nothing from it is shown
    /// or acted on under the new route.
    /// </remarks>
    private protected override void OnRouteKeyChanged()
    {
        _group = null;
        _settings = null;
        _savedSettings = null;
        _dagLayout = null;
        _triggerError = null;
        _manifestCount = 0;
        _completedCount = 0;
        _failedCount = 0;
        _inProgressCount = 0;
    }

    /// <summary>
    /// Loads the group, its manifest count and completed, failed and in-progress run counts, and
    /// the one-hop group dependency graph. The group's settings are not overwritten while the user
    /// has unsaved edits. Leaves the page empty when no group has the id.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the page is disposed or a newer load starts.</param>
    private protected override async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        using var context = await DataContextFactory.CreateDbContextAsync(cancellationToken);

        var groupId = ManifestGroupId;
        var freshGroup = await context
            .ManifestGroups.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

        if (freshGroup is null)
        {
            OnRouteKeyChanged();
            return;
        }

        _group = freshGroup;

        // The form keeps unsaved edits across polls; anything else (a clean form, or a form
        // left over from another group) takes the values just loaded.
        if (!IsSettingsDirty || _savedSettings?.GroupId != groupId)
        {
            _savedSettings = GroupSettings.From(freshGroup);
            _settings = _savedSettings with { };
        }

        // The same counts the API's group stats return, from the same service call.
        var stats = (
            await OperationsService.GetManifestGroupExecutionStatsAsync(
                [groupId],
                cancellationToken
            )
        ).Single();
        _manifestCount = stats.ManifestCount;
        _completedCount = stats.Completed;
        _failedCount = stats.Failed;
        _inProgressCount = stats.InProgress;

        // Build 1-hop neighborhood dependency graph
        await LoadDependencyGraph(context, cancellationToken);

        // Tell grids to reload their current page from the server
        if (_manifestsGrid is not null)
            await _manifestsGrid.ReloadAsync();
        if (_executionsGrid is not null)
            await _executionsGrid.ReloadAsync();
    }

    // ── Server-side grid callbacks ──

    // Both grids read rows without the columns they do not show: a manifest's properties and
    // exclusions, a run's input, output and stack trace.
    private Task<ServerDataResult<ManifestRow>> LoadManifestPageAsync(
        LoadDataArgs args,
        CancellationToken cancellationToken
    ) =>
        DataGridQueryHelper.LoadPageAsync(
            DataContextFactory,
            db =>
                db.Manifests.AsNoTracking()
                    .Where(m => m.ManifestGroupId == ManifestGroupId)
                    .OrderByDescending(m => m.Id),
            ManifestRow.Projection,
            args,
            _manifestsCount,
            ManifestGroupId,
            cancellationToken
        );

    private Task<ServerDataResult<RunRow>> LoadExecutionPageAsync(
        LoadDataArgs args,
        CancellationToken cancellationToken
    ) =>
        DataGridQueryHelper.LoadPageAsync(
            DataContextFactory,
            db => GridQueries.RunsOfGroup(db, ManifestGroupId),
            RunRow.Projection,
            args,
            _executionsCount,
            ManifestGroupId,
            cancellationToken
        );

    // ── Dependency graph ──

    private async Task LoadDependencyGraph(
        Effect.Data.Services.DataContext.IDataContext context,
        CancellationToken cancellationToken
    )
    {
        // Source the graph from the shared OperationsService so the dashboard's DAG and
        // the GraphQL `operations.manifestGroups.graph` query produce identical results.
        // The IDataContext parameter is retained for signature compatibility but the
        // service opens its own context.
        _ = context;

        var graph = await OperationsService.GetManifestGroupDependencyGraphAsync(
            ManifestGroupId,
            cancellationToken
        );

        // Single-node graphs (focal group only, no cross-group dependencies) collapse to
        // null here so the UI hides the DAG section entirely, matching the previous
        // dashboard behaviour where an isolated group rendered nothing.
        if (graph is null || graph.Edges.Count == 0)
        {
            _dagLayout = null;
            return;
        }

        var dagNodes = graph
            .Nodes.Select(n => new DagNode
            {
                Id = n.Id,
                Label = n.Name,
                IsHighlighted = n.IsHighlighted,
            })
            .ToList();

        var dagEdges = graph
            .Edges.Select(e => new DagEdge { FromId = e.FromId, ToId = e.ToId })
            .ToList();

        _dagLayout = DagLayoutEngine.ComputeLayout(dagNodes, dagEdges);
    }

    // ── Settings ──

    private async Task SaveSettings()
    {
        if (
            _settings is not { } edited
            || _savedSettings is not { } saved
            || edited.GroupId != ManifestGroupId
        )
            return;

        // What this save sends, frozen now: the operator may keep editing while it runs.
        var sent = edited with
        { };

        try
        {
            // Translate the edits into a patch input for the shared service. MaxActiveJobs needs
            // the explicit Clear flag because int? can't distinguish "unset" from "set to null"
            // in the patch record.
            var maxActiveJobsChanged = sent.MaxActiveJobs != saved.MaxActiveJobs;
            var input = new UpdateManifestGroupInput(
                MaxActiveJobs: maxActiveJobsChanged ? sent.MaxActiveJobs : null,
                ClearMaxActiveJobs: maxActiveJobsChanged && sent.MaxActiveJobs is null,
                Priority: sent.Priority != saved.Priority ? sent.Priority : null,
                IsEnabled: sent.IsEnabled != saved.IsEnabled ? sent.IsEnabled : null
            );

            var result = await OperationsService.UpdateManifestGroupAsync(
                sent.GroupId,
                input,
                DisposalToken
            );

            if (!result.Success)
            {
                NotificationService.Notify(
                    new NotificationMessage
                    {
                        Severity = NotificationSeverity.Error,
                        Summary = "Save Failed",
                        Detail = result.Message ?? "Update failed.",
                        Duration = 6000,
                    }
                );
                return;
            }

            // The saved values are now the baseline: later saves send only later edits. When
            // nothing was edited during the save the form is clean, and the reload replaces it
            // with what the database holds, including changes made by anyone else.
            if (_savedSettings?.GroupId == sent.GroupId)
                _savedSettings = sent;
            await LoadDataAsync(DisposalToken);

            NotificationService.Notify(
                new NotificationMessage
                {
                    Severity = NotificationSeverity.Success,
                    Summary = "Settings Saved",
                    Detail = $"Group \"{_group?.Name}\" settings updated.",
                    Duration = 4000,
                }
            );
        }
        catch (Exception ex)
        {
            NotificationService.Notify(
                new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "Save Failed",
                    Detail = ex.Message,
                    Duration = 6000,
                }
            );
        }
    }

    private void ResetSettings()
    {
        if (_savedSettings is null)
            return;

        _settings = _savedSettings with { };
    }

    private async Task TriggerGroup()
    {
        if (_group is null)
            return;

        _triggerError = null;
        _triggering = true;

        try
        {
            var count = await TraxScheduler.TriggerGroupAsync(ManifestGroupId);

            NotificationService.Notify(
                NotificationSeverity.Success,
                "Group Queued",
                $"{count} manifest(s) in \"{_group?.Name}\" queued for execution.",
                duration: 4000
            );
        }
        catch (Exception ex)
        {
            _triggerError = ex.Message;
        }
        finally
        {
            _triggering = false;
        }
    }

    private async Task CancelAllRunning()
    {
        if (_group is null)
            return;

        _cancellingAll = true;

        try
        {
            // The scheduler's own group cancel: it flags every in-progress run in the group and
            // signals the ones running on this server, the same call the API makes.
            var count = await TraxScheduler.CancelGroupAsync(ManifestGroupId, DisposalToken);

            if (count == 0)
            {
                NotificationService.Notify(
                    NotificationSeverity.Info,
                    "No Running Trains",
                    "There are no in-progress trains in this group.",
                    duration: 4000
                );
                return;
            }

            NotificationService.Notify(
                NotificationSeverity.Success,
                "Cancellation Requested",
                $"Cancel signal sent for {count} train(s).",
                duration: 4000
            );
        }
        catch (Exception ex)
        {
            _triggerError = ex.Message;
        }
        finally
        {
            _cancellingAll = false;
        }
    }

    private void OnDagNodeClick(long groupId)
    {
        Navigation.NavigateTo($"trax/data/manifest-groups/{groupId}");
    }
}
