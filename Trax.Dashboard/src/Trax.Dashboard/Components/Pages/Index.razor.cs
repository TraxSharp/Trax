using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Radzen;
using Trax.Dashboard.Components.Shared;
using Trax.Dashboard.Models;
using Trax.Dashboard.Utilities;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using static Trax.Dashboard.Utilities.DashboardFormatters;

namespace Trax.Dashboard.Components.Pages;

public partial class Index
{
    private const string TimeRange1H = "1h";
    private const string TimeRange24H = "24h";

    [Inject]
    private IDataContextProviderFactory DataContextFactory { get; set; } = default!;

    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    // KPI card values
    private int _executionsToday;
    private double _successRate;
    private int _currentlyRunning;
    private int _unresolvedDeadLetters;

    // Chart data
    private List<ExecutionTimePoint> _executionsOverTime = [];
    private List<ExecutionTimePoint> _hourlyData = [];
    private List<ExecutionTimePoint> _minuteData = [];
    private string _selectedTimeRange = TimeRange24H;
    private List<TrainFailureCount> _topFailures = [];
    private List<TrainDuration> _avgDurations = [];

    // Tables
    private List<Metadata> _recentFailures = [];

    // Server health
    private double _cpuPercent;
    private double _memoryWorkingSetMb;
    private double _gcHeapMb;
    private TimeSpan _uptime;
    private TimeSpan _prevCpuTime;
    private DateTime _prevSampleTime = DateTime.UtcNow;

    protected override async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        await DashboardSettings.InitializeAsync();

        CollectServerHealthMetrics();

        using var context = await DataContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var todayStart = now.Date;

        var hideAdmin = DashboardSettings.HideAdminTrains;
        var adminNames = DashboardSettings.AdminTrainNames;

        // Summary cards — single GroupBy for today's state counts
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

        // Executions over time (last 24h, grouped by hour)
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

        _hourlyData = Enumerable
            .Range(0, 24)
            .Select(i =>
            {
                var hourStart = now.AddHours(-23 + i);
                var targetDate = hourStart.Date;
                var targetHour = hourStart.Hour;
                return new ExecutionTimePoint
                {
                    Label = hourStart.ToString("HH"),
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

        // Per-minute data (last 60 minutes)
        var last60m = now.AddMinutes(-60);
        var minuteQuery = context.Metadatas.AsNoTracking().Where(m => m.StartTime >= last60m);

        if (hideAdmin)
            minuteQuery = minuteQuery.ExcludeAdmin(adminNames);

        var minuteStats = await minuteQuery
            .GroupBy(m => new
            {
                m.StartTime.Date,
                m.StartTime.Hour,
                m.StartTime.Minute,
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

        _minuteData = Enumerable
            .Range(0, 60)
            .Select(i =>
            {
                var minuteStart = now.AddMinutes(-59 + i);
                var targetDate = minuteStart.Date;
                var targetHour = minuteStart.Hour;
                var targetMinute = minuteStart.Minute;
                return new ExecutionTimePoint
                {
                    Label = i % 10 == 0 ? minuteStart.ToString("HH:mm") : " ",
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
                    Cancelled = minuteStats
                        .Where(x =>
                            x.Date == targetDate
                            && x.Hour == targetHour
                            && x.Minute == targetMinute
                            && x.TrainState == TrainState.Cancelled
                        )
                        .Sum(x => x.Count),
                };
            })
            .ToList();

        _executionsOverTime = _selectedTimeRange == TimeRange1H ? _minuteData : _hourlyData;

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

        // Average duration by train (completed in last 7 days)
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
    }

    private void OnTimeRangeChanged(string value)
    {
        _selectedTimeRange = value;
        _executionsOverTime = _selectedTimeRange == TimeRange1H ? _minuteData : _hourlyData;
    }

    private void OnRecentFailureRowClick(DataGridRowMouseEventArgs<Metadata> args)
    {
        Navigation.NavigateTo($"trax/data/metadata/{args.Data.Id}");
    }

    private void CollectServerHealthMetrics()
    {
        using var process = Process.GetCurrentProcess();
        var now = DateTime.UtcNow;

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

        _memoryWorkingSetMb = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1);
        _gcHeapMb = Math.Round(GC.GetTotalMemory(false) / 1024.0 / 1024.0, 1);

        _uptime = now - process.StartTime.ToUniversalTime();
    }
}
