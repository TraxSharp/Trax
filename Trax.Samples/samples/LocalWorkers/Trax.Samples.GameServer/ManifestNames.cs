namespace Trax.Samples.GameServer;

/// <summary>
/// Centralized manifest external IDs for the game server scheduler topology.
/// These names link the topology registration in Program.cs with runtime activation in train steps.
/// </summary>
public static class ManifestNames
{
    // ── Leaderboard ────────────────────────────────────────────────────
    public const string RecalculateLeaderboard = "recalculate-leaderboard";
    public const string GenerateSeasonReport = "generate-season-report";

    // ── Rewards ────────────────────────────────────────────────────────
    public const string DistributeDailyRewards = "distribute-daily-rewards";

    // ── Player Maintenance ─────────────────────────────────────────────
    public const string CleanupInactivePlayers = "cleanup-inactive-players";

    // ── Match Processing (batch per region) ────────────────────────────
    public const string ProcessMatch = "process-match";
    public const string DetectCheat = "detect-cheat";

    // ── Failure Demo ───────────────────────────────────────────────────
    public const string CorruptedDataRepair = "corrupted-data-repair";

    // ── Misfire Policy Showcase ────────────────────────────────────────
    public const string MisfireFireOnce = "misfire-fire-once";
    public const string MisfireDoNothing = "misfire-do-nothing";

    // ── One-Off Jobs ───────────────────────────────────────────────────
    public const string WelcomeBonus = "welcome-bonus";

    // ── Schedule Variance ──────────────────────────────────────────────
    public const string VarianceInterval = "variance-interval";
    public const string VarianceCron = "variance-cron";

    // ── Exclusion Windows ──────────────────────────────────────────────
    public const string WeekdayLeaderboard = "weekday-leaderboard";

    // ── Regions ────────────────────────────────────────────────────────
    public static readonly string[] Regions = ["na", "eu", "ap"];

    /// <summary>
    /// Constructs an indexed external ID: "{name}-{suffix}".
    /// Matches the topology naming convention used by ScheduleMany/IncludeMany.
    /// </summary>
    public static string WithIndex(string name, string suffix) => $"{name}-{suffix}";
}
