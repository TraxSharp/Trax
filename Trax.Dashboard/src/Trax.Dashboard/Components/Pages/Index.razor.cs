using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Trax.Dashboard.Components.Shared;
using Trax.Dashboard.Models;
using Trax.Dashboard.Utilities;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Metadata;
using Trax.Mediator.Services.TrainDiscovery;
using static Trax.Dashboard.Utilities.DashboardFormatters;

namespace Trax.Dashboard.Components.Pages;

public partial class Index
{
    [Inject]
    private IDataContextProviderFactory DataContextFactory { get; set; } = default!;

    [Inject]
    private ITrainDiscoveryService TrainDiscovery { get; set; } = default!;

    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = default!;

    // Summary card values
    private int _executionsToday;
    private double _successRate;
    private int _currentlyRunning;
    private int _unresolvedDeadLetters;
    private int _activeManifests;
    private int _registeredTrains;

    // Real-time metrics
    private int _queueDepth;
    private double _completedPerMinute;
    private double _failedPerMinute;
    private List<ThroughputMetric> _throughputData = [];

    // Chart data
    private List<ExecutionTimePoint> _executionsOverTime = [];
    private List<StateCount> _stateCounts = [];
    private List<TrainFailureCount> _topFailures = [];
    private List<TrainDuration> _avgDurations = [];

    // Tables
    private List<Metadata> _recentFailures = [];
    private List<Manifest> _activeManifestList = [];

    // Server health
    private double _cpuPercent;
    private double _memoryWorkingSetMb;
    private double _gcHeapMb;
    private int _threadCount;
    private TimeSpan _uptime;
    private int _gcGen0;
    private int _gcGen1;
    private int _gcGen2;
    private TimeSpan _prevCpuTime;
    private DateTime _prevSampleTime = DateTime.UtcNow;

    protected override async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        await DashboardSettings.InitializeAsync();

        // Server health metrics
        CollectServerHealthMetrics();

        using var context = await DataContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var todayStart = now.Date;

        var hideAdmin = DashboardSettings.HideAdminTrains;
        var adminNames = DashboardSettings.AdminTrainNames;

        // Summary cards — single GroupBy instead of materializing all today's metadata
        var todayQuery = context.Metadatas.AsNoTracking().Where(m => m.StartTime >= todayStart);

        if (hideAdmin)
            todayQuery = todayQuery.ExcludeAdmin(adminNames);

        var todayStateCounts = await todayQuery
            .GroupBy(m => m.TrainState)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int CountForState(TrainState s) =>
            todayStateCounts.FirstOrDefault(x => x.State == s)?.Count ?? 0;

        _executionsToday = todayStateCounts.Sum(x => x.Count);

        var completed = CountForState(TrainState.Completed);
        var terminal = completed + CountForState(TrainState.Failed);
        _successRate = terminal > 0 ? Math.Round(100.0 * completed / terminal, 1) : 0;
        var runningQuery = context
            .Metadatas.AsNoTracking()
            .Where(m => m.TrainState == TrainState.InProgress);

        if (hideAdmin)
            runningQuery = runningQuery.ExcludeAdmin(adminNames);

        _currentlyRunning = await runningQuery.CountAsync(cancellationToken);

        _unresolvedDeadLetters = await context
            .DeadLetters.AsNoTracking()
            .CountAsync(d => d.Status == DeadLetterStatus.AwaitingIntervention, cancellationToken);

        var activeManifestsQuery = context.Manifests.AsNoTracking().Where(m => m.IsEnabled);

        if (hideAdmin)
            activeManifestsQuery = activeManifestsQuery.ExcludeAdmin(adminNames);

        _activeManifests = await activeManifestsQuery.CountAsync(cancellationToken);

        var allTrains = TrainDiscovery.DiscoverTrains();
        _registeredTrains = hideAdmin
            ? allTrains.Count(w => !adminNames.Contains(w.ImplementationTypeName))
            : allTrains.Count;

        // Executions over time (last 24h, grouped by hour) — aggregated in SQL
        var last24h = now.AddHours(-24);
        var recentQuery = context.Metadatas.AsNoTracking().Where(m => m.StartTime >= last24h);

        if (hideAdmin)
            recentQuery = recentQuery.ExcludeAdmin(adminNames);

        var hourlyStats = await recentQuery
            .GroupBy(m => new
            {
                m.StartTime.Date,
                m.StartTime.Hour,
                m.TrainState,
            })
            .Select(g => new
            {
                g.Key.Date,
                g.Key.Hour,
                g.Key.TrainState,
                Count = g.Count(),
            })
            .ToListAsync(cancellationToken);

        _executionsOverTime = Enumerable
            .Range(0, 24)
            .Select(i =>
            {
                var hourStart = now.AddHours(-23 + i);
                var targetDate = hourStart.Date;
                var targetHour = hourStart.Hour;
                return new ExecutionTimePoint
                {
                    Hour = hourStart.ToString("HH"),
                    Completed = hourlyStats
                        .Where(x =>
                            x.Date == targetDate
                            && x.Hour == targetHour
                            && x.TrainState == TrainState.Completed
                        )
                        .Sum(x => x.Count),
                    Failed = hourlyStats
                        .Where(x =>
                            x.Date == targetDate
                            && x.Hour == targetHour
                            && x.TrainState == TrainState.Failed
                        )
                        .Sum(x => x.Count),
                    Cancelled = hourlyStats
                        .Where(x =>
                            x.Date == targetDate
                            && x.Hour == targetHour
                            && x.TrainState == TrainState.Cancelled
                        )
                        .Sum(x => x.Count),
                };
            })
            .ToList();

        // State breakdown — derived from the GroupBy above, no extra query
        _stateCounts =
        [
            new() { State = "Completed", Count = CountForState(TrainState.Completed) },
            new() { State = "Failed", Count = CountForState(TrainState.Failed) },
            new() { State = "In Progress", Count = CountForState(TrainState.InProgress) },
            new() { State = "Pending", Count = CountForState(TrainState.Pending) },
            new() { State = "Cancelled", Count = CountForState(TrainState.Cancelled) },
        ];

        // Top failing trains (last 7 days)
        var last7d = now.AddDays(-7);
        var failuresQuery = context
            .Metadatas.AsNoTracking()
            .Where(m => m.TrainState == TrainState.Failed && m.StartTime >= last7d);

        if (hideAdmin)
            failuresQuery = failuresQuery.ExcludeAdmin(adminNames);

        _topFailures = (
            await failuresQuery
                .GroupBy(m => m.Name)
                .Select(g => new TrainFailureCount { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync(cancellationToken)
        )
            .Select(x => new TrainFailureCount { Name = ShortName(x.Name), Count = x.Count })
            .ToList();

        // Average duration by train (completed in last 7 days) — aggregated in SQL
        var durationsQuery = context
            .Metadatas.AsNoTracking()
            .Where(m =>
                m.TrainState == TrainState.Completed
                && m.EndTime != null
                && m.StartTime >= last7d
                && m.ParentId == null
            );

        if (hideAdmin)
            durationsQuery = durationsQuery.ExcludeAdmin(adminNames);

        var avgDurationData = await durationsQuery
            .GroupBy(m => m.Name)
            .Select(g => new
            {
                Name = g.Key,
                AvgSeconds = g.Average(m => (m.EndTime!.Value - m.StartTime).TotalSeconds),
            })
            .OrderByDescending(x => x.AvgSeconds)
            .Take(10)
            .ToListAsync(cancellationToken);

        _avgDurations = avgDurationData
            .Select(x => new TrainDuration
            {
                Name = ShortName(x.Name),
                AvgMs = Math.Round(x.AvgSeconds * 1000, 0),
            })
            .ToList();

        // Recent failures
        var recentFailuresQuery = context
            .Metadatas.AsNoTracking()
            .Where(m => m.TrainState == TrainState.Failed);

        if (hideAdmin)
            recentFailuresQuery = recentFailuresQuery.ExcludeAdmin(adminNames);

        _recentFailures = await recentFailuresQuery
            .OrderByDescending(m => m.StartTime)
            .Take(20)
            .ToListAsync(cancellationToken);

        // Active scheduled manifests
        var activeManifestListQuery = context
            .Manifests.AsNoTracking()
            .Where(m => m.IsEnabled && m.ScheduleType != ScheduleType.None);

        if (hideAdmin)
            activeManifestListQuery = activeManifestListQuery.ExcludeAdmin(adminNames);

        _activeManifestList = await activeManifestListQuery
            .OrderBy(m => m.Name)
            .Take(20)
            .ToListAsync(cancellationToken);

        // Real-time metrics — queue depth
        _queueDepth = await context
            .WorkQueues.AsNoTracking()
            .CountAsync(w => w.Status == WorkQueueStatus.Queued, cancellationToken);

        // Throughput (5-minute rolling window)
        var fiveMinAgo = now.AddMinutes(-5);
        var throughputQuery = context.Metadatas.AsNoTracking().Where(m => m.EndTime >= fiveMinAgo);

        if (hideAdmin)
            throughputQuery = throughputQuery.ExcludeAdmin(adminNames);

        var recentTerminal = await throughputQuery
            .GroupBy(m => m.TrainState)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var completedLast5 =
            recentTerminal.FirstOrDefault(x => x.State == TrainState.Completed)?.Count ?? 0;
        var failedLast5 =
            recentTerminal.FirstOrDefault(x => x.State == TrainState.Failed)?.Count ?? 0;
        _completedPerMinute = Math.Round(completedLast5 / 5.0, 1);
        _failedPerMinute = Math.Round(failedLast5 / 5.0, 1);

        // Per-minute throughput chart (last 60 minutes)
        var last60m = now.AddMinutes(-60);
        var minuteQuery = context
            .Metadatas.AsNoTracking()
            .Where(m =>
                m.EndTime >= last60m
                && (m.TrainState == TrainState.Completed || m.TrainState == TrainState.Failed)
            );

        if (hideAdmin)
            minuteQuery = minuteQuery.ExcludeAdmin(adminNames);

        var minuteStats = await minuteQuery
            .GroupBy(m => new
            {
                m.EndTime!.Value.Date,
                m.EndTime!.Value.Hour,
                m.EndTime!.Value.Minute,
                m.TrainState,
            })
            .Select(g => new
            {
                g.Key.Date,
                g.Key.Hour,
                g.Key.Minute,
                g.Key.TrainState,
                Count = g.Count(),
            })
            .ToListAsync(cancellationToken);

        _throughputData = Enumerable
            .Range(0, 60)
            .Select(i =>
            {
                var minuteStart = now.AddMinutes(-59 + i);
                var targetDate = minuteStart.Date;
                var targetHour = minuteStart.Hour;
                var targetMinute = minuteStart.Minute;
                return new ThroughputMetric
                {
                    Minute = i % 10 == 0 ? minuteStart.ToString("HH:mm") : " ",
                    Completed = minuteStats
                        .Where(x =>
                            x.Date == targetDate
                            && x.Hour == targetHour
                            && x.Minute == targetMinute
                            && x.TrainState == TrainState.Completed
                        )
                        .Sum(x => x.Count),
                    Failed = minuteStats
                        .Where(x =>
                            x.Date == targetDate
                            && x.Hour == targetHour
                            && x.Minute == targetMinute
                            && x.TrainState == TrainState.Failed
                        )
                        .Sum(x => x.Count),
                };
            })
            .ToList();
    }

    private void CollectServerHealthMetrics()
    {
        using var process = Process.GetCurrentProcess();
        var now = DateTime.UtcNow;

        // CPU % — delta between samples, normalized by processor count
        var currentCpuTime = process.TotalProcessorTime;
        var elapsed = (now - _prevSampleTime).TotalMilliseconds;

        if (elapsed > 0 && _prevCpuTime != TimeSpan.Zero)
        {
            var cpuDelta = (currentCpuTime - _prevCpuTime).TotalMilliseconds;
            _cpuPercent = Math.Round(cpuDelta / elapsed / Environment.ProcessorCount * 100, 1);
            _cpuPercent = Math.Clamp(_cpuPercent, 0, 100);
        }

        _prevCpuTime = currentCpuTime;
        _prevSampleTime = now;

        // Memory
        _memoryWorkingSetMb = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1);
        _gcHeapMb = Math.Round(GC.GetTotalMemory(false) / 1024.0 / 1024.0, 1);

        // Threads & uptime
        _threadCount = process.Threads.Count;
        _uptime = now - process.StartTime.ToUniversalTime();

        // GC collections
        _gcGen0 = GC.CollectionCount(0);
        _gcGen1 = GC.CollectionCount(1);
        _gcGen2 = GC.CollectionCount(2);
    }
}
