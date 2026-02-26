namespace Trax.Samples.Scheduler;

/// <summary>
/// Centralized manifest external IDs and table names for the scheduler sample topology.
/// These names link the topology registration in Program.cs with runtime activation in workflow steps.
/// </summary>
public static class ManifestNames
{
    // ── Simple Schedules ─────────────────────────────────────────────────
    public const string HelloWorld = "hello-world";
    public const string GoodbyeNightly = "goodbye-nightly";
    public const string AlwaysFails = "always-fails";

    // ── Dependency Topology ──────────────────────────────────────────────
    public const string HelloGreeter = "hello-greeter";
    public const string FarewellA = "farewell-a";
    public const string FarewellB = "farewell-b";
    public const string FarewellC = "farewell-c";
    public const string Broadcast = "broadcast";

    // ── Customer ETL Pipeline ────────────────────────────────────────────
    public const string ExtractCustomer = "extract-customer";
    public const string TransformCustomer = "transform-customer";
    public const string DqCustomer = "dq-customer";

    // ── Transaction Pipeline (with dormant dependents) ───────────────────
    public const string ExtractTransaction = "extract-transaction";
    public const string DqTransaction = "dq-transaction";

    // ── Table Names ──────────────────────────────────────────────────────
    public const string CustomerTable = "Customer";
    public const string TransactionTable = "Transaction";

    /// <summary>
    /// Constructs an indexed external ID: "{name}-{index}".
    /// Matches the topology naming convention used by ScheduleMany/IncludeMany.
    /// </summary>
    public static string WithIndex(string name, int index) => $"{name}-{index}";
}
