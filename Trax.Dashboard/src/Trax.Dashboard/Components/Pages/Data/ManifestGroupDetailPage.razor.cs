using System.Linq.Dynamic.Core;
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

    [Parameter]
    public long ManifestGroupId { get; set; }

    protected override object? GetRouteKey() => ManifestGroupId;

    private ManifestGroup? _group;
    private DagLayout? _dagLayout;
    private bool _triggering;
    private string? _triggerError;
    private bool _cancellingAll;

    // ── Summary counts (efficient DB aggregates) ──
    private int _manifestCount;
    private int _completedCount;
    private int _failedCount;
    private int _inProgressCount;

    // ── Grid references for server-side reload ──
    private TraxDataGrid<Manifest>? _manifestsGrid;
    private TraxDataGrid<Metadata>? _executionsGrid;

    // ── Settings dirty tracking ──
    private int? _savedMaxActiveJobs;
    private int _savedPriority;
    private bool _savedIsEnabled;

    private bool IsSettingsDirty =>
        _group is not null
        && (
            _group.MaxActiveJobs != _savedMaxActiveJobs
            || _group.Priority != _savedPriority
            || _group.IsEnabled != _savedIsEnabled
        );

    protected override async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        using var context = await DataContextFactory.CreateDbContextAsync(cancellationToken);

        var freshGroup = await context.ManifestGroups.FirstOrDefaultAsync(
            g => g.Id == ManifestGroupId,
            cancellationToken
        );

        if (freshGroup is null)
        {
            _group = null;
            _manifestCount = 0;
            _completedCount = 0;
            _failedCount = 0;
            _inProgressCount = 0;
            _dagLayout = null;
            return;
        }

        // Don't overwrite the user's unsaved edits during poll ticks
        if (!IsSettingsDirty)
        {
            _group = freshGroup;
            SnapshotSettings();
        }

        // Efficient COUNTs for summary cards
        _manifestCount = await context
            .Manifests.AsNoTracking()
            .CountAsync(m => m.ManifestGroupId == ManifestGroupId, cancellationToken);

        // Subquery for scoping execution counts to this group's manifests.
        // No AsNoTracking — this is composed into outer queries, never materialized.
        var manifestIdsSubquery = context
            .Manifests.Where(m => m.ManifestGroupId == ManifestGroupId)
            .Select(m => m.Id);

        var executionsBase = context
            .Metadatas.AsNoTracking()
            .Where(m => m.ManifestId.HasValue && manifestIdsSubquery.Contains(m.ManifestId.Value));

        _completedCount = await executionsBase.CountAsync(
            m => m.TrainState == TrainState.Completed,
            cancellationToken
        );
        _failedCount = await executionsBase.CountAsync(
            m => m.TrainState == TrainState.Failed,
            cancellationToken
        );
        _inProgressCount = await executionsBase.CountAsync(
            m => m.TrainState == TrainState.InProgress,
            cancellationToken
        );

        // Build 1-hop neighborhood dependency graph
        await LoadDependencyGraph(context, cancellationToken);

        // Tell grids to reload their current page from the server
        if (_manifestsGrid is not null)
            await _manifestsGrid.ReloadAsync();
        if (_executionsGrid is not null)
            await _executionsGrid.ReloadAsync();
    }

    // ── Server-side grid callbacks ──

    private async Task<ServerDataResult<Manifest>> LoadManifestPageAsync(
        LoadDataArgs args,
        CancellationToken cancellationToken
    )
    {
        using var context = await DataContextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<Manifest> query = context
            .Manifests.AsNoTracking()
            .Where(m => m.ManifestGroupId == ManifestGroupId);

        if (!string.IsNullOrEmpty(args.Filter))
            query = query.Where(args.Filter);

        if (!string.IsNullOrEmpty(args.OrderBy))
            query = query.OrderBy(args.OrderBy);
        else
            query = query.OrderByDescending(m => m.Id);

        var count = await query.CountAsync(cancellationToken);

        if (args.Skip.HasValue)
            query = query.Skip(args.Skip.Value);
        if (args.Top.HasValue)
            query = query.Take(args.Top.Value);

        var items = await query.ToListAsync(cancellationToken);
        return new ServerDataResult<Manifest>(items, count);
    }

    private async Task<ServerDataResult<Metadata>> LoadExecutionPageAsync(
        LoadDataArgs args,
        CancellationToken cancellationToken
    )
    {
        using var context = await DataContextFactory.CreateDbContextAsync(cancellationToken);

        // Subquery — generates SQL subselect, not a materialized IN list.
        // No AsNoTracking — this is composed into the outer query, never materialized.
        var manifestIdsSubquery = context
            .Manifests.Where(m => m.ManifestGroupId == ManifestGroupId)
            .Select(m => m.Id);

        IQueryable<Metadata> query = context
            .Metadatas.AsNoTracking()
            .Where(m => m.ManifestId.HasValue && manifestIdsSubquery.Contains(m.ManifestId.Value));

        if (!string.IsNullOrEmpty(args.Filter))
            query = query.Where(args.Filter);

        if (!string.IsNullOrEmpty(args.OrderBy))
            query = query.OrderBy(args.OrderBy);
        else
            query = query.OrderByDescending(m => m.StartTime);

        var count = await query.CountAsync(cancellationToken);

        if (args.Skip.HasValue)
            query = query.Skip(args.Skip.Value);
        if (args.Top.HasValue)
            query = query.Take(args.Top.Value);

        var items = await query.ToListAsync(cancellationToken);
        return new ServerDataResult<Metadata>(items, count);
    }

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

    private void SnapshotSettings()
    {
        if (_group is null)
            return;

        _savedMaxActiveJobs = _group.MaxActiveJobs;
        _savedPriority = _group.Priority;
        _savedIsEnabled = _group.IsEnabled;
    }

    private async Task SaveSettings()
    {
        if (_group is null)
            return;

        try
        {
            // Translate the dirty in-memory edits into a patch input for the shared
            // service. MaxActiveJobs needs the explicit Clear flag because int? can't
            // distinguish "unset" from "set to null" in the patch record.
            var maxActiveJobsChanged = _group.MaxActiveJobs != _savedMaxActiveJobs;
            var input = new UpdateManifestGroupInput(
                MaxActiveJobs: maxActiveJobsChanged ? _group.MaxActiveJobs : null,
                ClearMaxActiveJobs: maxActiveJobsChanged && _group.MaxActiveJobs is null,
                Priority: _group.Priority != _savedPriority ? _group.Priority : null,
                IsEnabled: _group.IsEnabled != _savedIsEnabled ? _group.IsEnabled : null
            );

            var result = await OperationsService.UpdateManifestGroupAsync(
                _group.Id,
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

            // Reload to pick up the bumped UpdatedAt and confirm persistence.
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
        if (_group is null)
            return;

        _group.MaxActiveJobs = _savedMaxActiveJobs;
        _group.Priority = _savedPriority;
        _group.IsEnabled = _savedIsEnabled;
    }

    private async Task TriggerGroup()
    {
        if (_group is null)
            return;

        _triggerError = null;
        _triggering = true;

        try
        {
            var count = await TraxScheduler.TriggerGroupAsync(_group.Id);

            NotificationService.Notify(
                NotificationSeverity.Success,
                "Group Queued",
                $"{count} manifest(s) in \"{_group.Name}\" queued for execution.",
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
            using var context = await DataContextFactory.CreateDbContextAsync(DisposalToken);

            var manifestIdsSubquery = context
                .Manifests.Where(m => m.ManifestGroupId == ManifestGroupId)
                .Select(m => m.Id);

            var inProgressIds = await context
                .Metadatas.AsNoTracking()
                .Where(m =>
                    m.ManifestId.HasValue
                    && manifestIdsSubquery.Contains(m.ManifestId.Value)
                    && m.TrainState == TrainState.InProgress
                )
                .Select(m => m.Id)
                .ToListAsync(DisposalToken);

            if (inProgressIds.Count == 0)
            {
                NotificationService.Notify(
                    NotificationSeverity.Info,
                    "No Running Trains",
                    "There are no in-progress trains in this group.",
                    duration: 4000
                );
                return;
            }

            var count = await CancellationHelper.CancelTrainsAsync(
                DataContextFactory,
                ServiceProvider,
                inProgressIds,
                DisposalToken
            );

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
