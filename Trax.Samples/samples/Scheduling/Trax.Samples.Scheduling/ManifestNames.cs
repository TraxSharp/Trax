namespace Trax.Samples.Scheduling;

/// <summary>
/// The external ids of the sample's manifests. A manifest's external id is its identity: the
/// scheduler upserts by it at every start, the dashboard and the GraphQL operations address a
/// manifest by it, and a parent activates a dormant dependent by it.
/// </summary>
public static class ManifestNames
{
    /// <summary>Interval manifest, and the parent of the two dependents below.</summary>
    public const string RefreshExchangeRates = "refresh-exchange-rates";

    /// <summary>Dependent: runs after every successful <see cref="RefreshExchangeRates"/>.</summary>
    public const string RepriceCatalog = "reprice-catalog";

    /// <summary>Dormant dependent: runs only when <see cref="RefreshExchangeRates"/> activates it.</summary>
    public const string AlertRateSpike = "alert-rate-spike";

    /// <summary>Cron manifest: every day at 07:00 UTC.</summary>
    public const string SendDailyDigest = "send-daily-digest";

    /// <summary>One-off manifest: runs once, shortly after the first start, then disables itself.</summary>
    public const string SendLaunchAnnouncement = "send-launch-announcement";

    /// <summary>Interval manifest that fails while its supplier is down, then dead-letters.</summary>
    public const string ImportSupplierFeed = "import-supplier-feed";
}
