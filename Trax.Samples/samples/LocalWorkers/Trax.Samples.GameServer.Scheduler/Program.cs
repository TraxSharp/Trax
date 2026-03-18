// ─────────────────────────────────────────────────────────────────────────────
// Trax Game Server — Scheduler
//
// Background scheduler that executes heavy game server operations:
// leaderboard recalculation, daily rewards, match processing, player cleanup,
// and anti-cheat detection. Run alongside the REST or GraphQL API process —
// the API queues work, the scheduler executes it.
//
// Includes the Trax Dashboard at http://localhost:5201/trax for monitoring.
//
// Prerequisites:
//   1. Start Postgres:  cd Trax.Samples && docker compose up -d
//   2. Pack local:      ./pack-local.sh
//   3. Run:             dotnet run --project samples/Trax.Samples.GameServer.Scheduler
//
// The dashboard shows manifests, executions, dead letters, and real-time status.
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Dashboard.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.GameServer;
using Trax.Samples.GameServer.Trains.Leaderboard.GenerateSeasonReport;
using Trax.Samples.GameServer.Trains.Leaderboard.RecalculateLeaderboard;
using Trax.Samples.GameServer.Trains.Maintenance.CorruptedDataRepair;
using Trax.Samples.GameServer.Trains.Matches.DetectCheatPattern;
using Trax.Samples.GameServer.Trains.Matches.ProcessMatchResult;
using Trax.Samples.GameServer.Trains.Players.BanPlayer;
using Trax.Samples.GameServer.Trains.Players.CleanupInactivePlayers;
using Trax.Samples.GameServer.Trains.Players.LookupPlayer;
using Trax.Samples.GameServer.Trains.Rewards.DistributeDailyRewards;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

builder.Services.AddLogging(logging => logging.AddConsole());

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .AddDataContextLogging()
                .AddJson()
                .SaveTrainParameters()
                .AddJunctionProgress()
        )
        .AddMediator(typeof(ManifestNames).Assembly)
        .AddScheduler(scheduler =>
            scheduler
                // ── Global Configuration ────────────────────────────────────────
                .AddMetadataCleanup(cleanup =>
                {
                    cleanup.AddTrainType<ILookupPlayerTrain>();
                    cleanup.AddTrainType<IBanPlayerTrain>();
                    cleanup.AddTrainType<ICleanupInactivePlayersTrain>();
                    cleanup.AddTrainType<IProcessMatchResultTrain>();
                    cleanup.AddTrainType<IDetectCheatPatternTrain>();
                    cleanup.AddTrainType<IRecalculateLeaderboardTrain>();
                    cleanup.AddTrainType<IGenerateSeasonReportTrain>();
                    cleanup.AddTrainType<IDistributeDailyRewardsTrain>();
                    cleanup.AddTrainType<ICorruptedDataRepairTrain>();
                })
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                // 1. INTERVAL + DEPENDENCY CHAIN
                //    Recalculate leaderboard every 5 minutes, then generate
                //    a season report once recalculation completes.
                //
                //    recalculate-leaderboard (every 5 min)
                //      └── generate-season-report (ThenInclude — depends on recalc)
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                .Schedule<IRecalculateLeaderboardTrain>(
                    ManifestNames.RecalculateLeaderboard,
                    new RecalculateLeaderboardInput { Region = "global" },
                    Every.Minutes(5)
                )
                .ThenInclude<IGenerateSeasonReportTrain>(
                    ManifestNames.GenerateSeasonReport,
                    new GenerateSeasonReportInput { Season = "Season 7" }
                )
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                // 2. CRON-BASED SCHEDULE
                //    Distribute daily login rewards at midnight.
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                .Schedule<IDistributeDailyRewardsTrain>(
                    ManifestNames.DistributeDailyRewards,
                    new DistributeDailyRewardsInput { RewardType = "LoginBonus" },
                    Cron.Daily(hour: 0)
                )
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                // 3. INTERVAL: Hourly player cleanup
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                .Schedule<ICleanupInactivePlayersTrain>(
                    ManifestNames.CleanupInactivePlayers,
                    new CleanupInactivePlayersInput { InactiveDays = 90 },
                    Every.Hours(1)
                )
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                // 4. BATCH SCHEDULING + DORMANT DEPENDENTS + VARIANCE
                //    ScheduleMany creates one ProcessMatchResult per region.
                //    IncludeMany creates dormant DetectCheatPattern dependents —
                //    they only fire when CheckForAnomaliesJunction activates them.
                //    WithVariance adds up to 2 minutes of random jitter to stagger
                //    region processing and avoid thundering-herd API calls.
                //
                //    process-match-{region} (every 5 min ± 2 min, priority 24, max 5)
                //      └── detect-cheat-{region} (Dormant — activated on anomalies)
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                .ScheduleMany<IProcessMatchResultTrain>(
                    ManifestNames.ProcessMatch,
                    ManifestNames.Regions.Select(region => new ManifestItem(
                        region,
                        new ProcessMatchResultInput
                        {
                            Region = region,
                            MatchId = $"batch-{region}",
                            WinnerId = "batch-winner",
                            LoserId = "batch-loser",
                            WinnerScore = 75,
                            LoserScore = 10,
                        }
                    )),
                    Every.Minutes(5).WithVariance(TimeSpan.FromMinutes(2)),
                    o => o.Priority(24).Group(group => group.MaxActiveJobs(5))
                )
                .IncludeMany<IDetectCheatPatternTrain>(
                    ManifestNames.DetectCheat,
                    ManifestNames.Regions.Select(region => new ManifestItem(
                        region,
                        new DetectCheatPatternInput
                        {
                            PlayerId = "unknown",
                            MatchId = "pending",
                            AnomalyCount = 0,
                        },
                        DependsOn: ManifestNames.WithIndex(ManifestNames.ProcessMatch, region)
                    )),
                    options: o => o.Dormant()
                )
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                // 5. RETRY POLICY & DEAD LETTERS
                //    This train always fails — dead-letters after 1 retry.
                //    Check the dashboard dead letter page for failure details.
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                .Schedule<ICorruptedDataRepairTrain>(
                    ManifestNames.CorruptedDataRepair,
                    new CorruptedDataRepairInput { TableName = "player_sessions" },
                    Every.Seconds(30),
                    o => o.MaxRetries(1)
                )
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                // 6. MISFIRE POLICIES
                //    Two policies compared side-by-side:
                //    - FireOnceNow: fires immediately on recovery (default)
                //    - DoNothing:   skips if overdue, waits for next tick
                //
                //    Try it: stop the scheduler for >60 seconds, restart.
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                .Schedule<IRecalculateLeaderboardTrain>(
                    ManifestNames.MisfireFireOnce,
                    new RecalculateLeaderboardInput { Region = "Misfire: FireOnceNow" },
                    Every.Seconds(30),
                    o => o.OnMisfire(MisfirePolicy.FireOnceNow)
                )
                .Schedule<IRecalculateLeaderboardTrain>(
                    ManifestNames.MisfireDoNothing,
                    new RecalculateLeaderboardInput { Region = "Misfire: DoNothing" },
                    Every.Seconds(30),
                    o =>
                        o.OnMisfire(MisfirePolicy.DoNothing)
                            .MisfireThreshold(TimeSpan.FromSeconds(10))
                )
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                // 7. ONE-OFF JOB
                //    Welcome bonus distributed once, 1 minute after startup.
                //    Auto-disables after firing.
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                .ScheduleOnce<IDistributeDailyRewardsTrain>(
                    ManifestNames.WelcomeBonus,
                    new DistributeDailyRewardsInput { RewardType = "WelcomeBonus" },
                    TimeSpan.FromMinutes(1)
                )
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                // 8. EXCLUSION WINDOWS
                //    Weekday-only leaderboard recalculation — skipped on weekends
                //    and during the daily maintenance window (2:00–4:00 AM).
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                .Schedule<IRecalculateLeaderboardTrain>(
                    ManifestNames.WeekdayLeaderboard,
                    new RecalculateLeaderboardInput { Region = "Weekday Report" },
                    Every.Seconds(30),
                    o =>
                        o.Exclude(Exclude.DaysOfWeek(DayOfWeek.Saturday, DayOfWeek.Sunday))
                            .Exclude(
                                Exclude.TimeWindow(TimeOnly.Parse("02:00"), TimeOnly.Parse("04:00"))
                            )
                )
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                // 9. SCHEDULE VARIANCE (JITTER)
                //    Adds random delay [0, variance] after each successful run.
                //    Useful for scrapers and API polling to avoid thundering herd
                //    and make access patterns less predictable.
                //
                //    Two examples:
                //    - Interval: every 30s + up to 15s jitter (fires between 30–45s)
                //    - Cron: daily at 3am + up to 30min jitter (fires 3:00–3:30am)
                //
                //    Check NextScheduledRun in the dashboard to see the pre-computed
                //    next fire time after each run.
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                .Schedule<IRecalculateLeaderboardTrain>(
                    ManifestNames.VarianceInterval,
                    new RecalculateLeaderboardInput { Region = "Variance: Interval" },
                    Every.Seconds(30).WithVariance(TimeSpan.FromSeconds(15))
                )
                .Schedule<IRecalculateLeaderboardTrain>(
                    ManifestNames.VarianceCron,
                    new RecalculateLeaderboardInput { Region = "Variance: Cron" },
                    Cron.Daily(hour: 3),
                    o => o.Variance(TimeSpan.FromMinutes(30))
                )
        )
);

builder.AddTraxDashboard();

var app = builder.Build();

app.UseTraxDashboard();

app.Run();
