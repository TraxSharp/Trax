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
using Trax.Scheduler.Services.CancellationRegistry;
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
        var currentManifestIdsQuery = context
            .Manifests.Where(m => m.ManifestGroupId == ManifestGroupId)
            .Select(m => m.Id);

        if (!await currentManifestIdsQuery.AnyAsync(cancellationToken))
        {
            _dagLayout = null;
            return;
        }

        // Upstream: groups containing manifests that our manifests depend on
        var upstreamGroupIds = await context
            .Manifests.AsNoTracking()
            .Where(m => m.ManifestGroupId == ManifestGroupId && m.DependsOnManifestId != null)
            .Join(
                context.Manifests.AsNoTracking(),
                dependent => dependent.DependsOnManifestId,
                parent => (long?)parent.Id,
                (dependent, parent) => parent.ManifestGroupId
            )
            .Where(parentGroupId => parentGroupId != ManifestGroupId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Downstream: groups containing manifests that depend on our manifests
        var downstreamGroupIds = await context
            .Manifests.AsNoTracking()
            .Where(m =>
                m.DependsOnManifestId != null
                && currentManifestIdsQuery.Contains(m.DependsOnManifestId.Value)
                && m.ManifestGroupId != ManifestGroupId
            )
            .Select(m => m.ManifestGroupId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var neighborGroupIds = upstreamGroupIds.Union(downstreamGroupIds).ToHashSet();

        if (neighborGroupIds.Count == 0)
        {
            _dagLayout = null;
            return;
        }

        var allRelevantGroupIds = neighborGroupIds.Append(ManifestGroupId).ToList();

        var neighborGroups = await context
            .ManifestGroups.AsNoTracking()
            .Where(g => allRelevantGroupIds.Contains(g.Id))
            .Select(g => new { g.Id, g.Name })
            .ToListAsync(cancellationToken);

        var dagNodes = neighborGroups
            .Select(g => new DagNode
            {
                Id = g.Id,
                Label = g.Name,
                IsHighlighted = g.Id == ManifestGroupId,
            })
            .ToList();

        // Edges between all relevant groups
        var crossGroupEdges = await context
            .Manifests.AsNoTracking()
            .Where(m =>
                m.DependsOnManifestId != null && allRelevantGroupIds.Contains(m.ManifestGroupId)
            )
            .Join(
                context.Manifests.AsNoTracking(),
                dependent => dependent.DependsOnManifestId,
                parent => (long?)parent.Id,
                (dependent, parent) =>
                    new
                    {
                        ParentGroupId = parent.ManifestGroupId,
                        DependentGroupId = dependent.ManifestGroupId,
                    }
            )
            .Where(e =>
                e.ParentGroupId != e.DependentGroupId
                && allRelevantGroupIds.Contains(e.ParentGroupId)
            )
            .Distinct()
            .ToListAsync(cancellationToken);

        var dagEdges = crossGroupEdges
            .Select(e => new DagEdge { FromId = e.ParentGroupId, ToId = e.DependentGroupId })
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
            using var context = await DataContextFactory.CreateDbContextAsync(DisposalToken);

            var entity = await context.ManifestGroups.FindAsync(_group.Id);
            if (entity is null)
                return;

            entity.MaxActiveJobs = _group.MaxActiveJobs;
            entity.Priority = _group.Priority;
            entity.IsEnabled = _group.IsEnabled;
            entity.UpdatedAt = DateTime.UtcNow;

            await context.SaveChanges(DisposalToken);

            SnapshotSettings();

            NotificationService.Notify(
                new NotificationMessage
                {
                    Severity = NotificationSeverity.Success,
                    Summary = "Settings Saved",
                    Detail = $"Group \"{_group.Name}\" settings updated.",
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

            // Get IDs of in-progress metadata for this group
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

            // Batch set cancel_requested = true
            await context
                .Metadatas.Where(m => inProgressIds.Contains(m.Id))
                .ExecuteUpdateAsync(
                    s => s.SetProperty(m => m.CancellationRequested, true),
                    DisposalToken
                );

            // Same-server instant cancel bonus
            var registry = ServiceProvider.GetService<ICancellationRegistry>();
            if (registry is not null)
            {
                foreach (var id in inProgressIds)
                    registry.TryCancel(id);
            }

            NotificationService.Notify(
                NotificationSeverity.Success,
                "Cancellation Requested",
                $"Cancel signal sent for {inProgressIds.Count} train(s).",
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
