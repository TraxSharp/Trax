using Microsoft.Extensions.Configuration;

namespace Trax.Samples.Scheduling.Services;

/// <summary>
/// A stand-in for a supplier's product feed that is down for its first few calls. With the
/// default of three failing calls and the manifest's <c>MaxRetries(2)</c>, the first run and both
/// retries fail, the manifest is dead-lettered, and the requeue of that dead letter succeeds
/// because the outage is over. Registered as a singleton.
/// </summary>
public class SupplierFeed(IConfiguration configuration)
{
    private readonly Lock _gate = new();
    private int _failuresLeft = configuration.GetValue("SupplierFeed:OutageCalls", 3);

    /// <summary>Starts a new outage that lasts <paramref name="failingCalls"/> calls.</summary>
    public void StartOutage(int failingCalls)
    {
        lock (_gate)
            _failuresLeft = failingCalls;
    }

    /// <summary>Returns the feed's products, or throws while the outage lasts.</summary>
    public IReadOnlyList<string> FetchProducts(string supplier)
    {
        lock (_gate)
        {
            if (_failuresLeft > 0)
            {
                _failuresLeft--;
                throw new HttpRequestException(
                    $"Supplier '{supplier}' answered 503 Service Unavailable."
                );
            }
        }

        return ["kettle", "toaster", "teapot"];
    }
}
