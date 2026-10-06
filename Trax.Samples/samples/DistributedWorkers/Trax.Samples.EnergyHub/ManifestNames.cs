namespace Trax.Samples.EnergyHub;

/// <summary>
/// Centralized manifest external IDs for the Somerset Energy Hub scheduler topology.
/// These names link the topology registration in Program.cs with runtime activation in train steps.
/// </summary>
public static class ManifestNames
{
    // ── Solar Production ─────────────────────────────────────────────
    public const string MonitorSolarProduction = "monitor-solar-production";

    // ── Battery Storage ──────────────────────────────────────────────
    public const string ManageBatteryStorage = "manage-battery-storage";

    // ── EV Charging ──────────────────────────────────────────────────
    public const string ProcessChargingSession = "process-charging-session";

    // ── Microgrid ────────────────────────────────────────────────────
    public const string OptimizeMicrogrid = "optimize-microgrid";

    // ── Grid Trading (UBOSS) ─────────────────────────────────────────
    public const string TradeGridEnergy = "trade-grid-energy";

    // ── Sustainability ───────────────────────────────────────────────
    public const string GenerateSustainabilityReport = "generate-sustainability-report";

    // ── Zones ────────────────────────────────────────────────────────
    public static readonly string[] Zones = ["plaza", "data-center", "parking"];

    /// <summary>
    /// Constructs an indexed external ID: "{name}-{suffix}".
    /// Matches the topology naming convention used by ScheduleMany/IncludeMany.
    /// </summary>
    public static string WithIndex(string name, string suffix) => $"{name}-{suffix}";
}
