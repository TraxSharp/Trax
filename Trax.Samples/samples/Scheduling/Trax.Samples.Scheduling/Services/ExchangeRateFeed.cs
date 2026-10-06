namespace Trax.Samples.Scheduling.Services;

/// <summary>
/// A stand-in for an exchange-rate API. Each reading moves the rate by a small amount, and every
/// sixth reading is a spike, so a running demo shows the dormant <c>alert-rate-spike</c> manifest
/// being activated about every 30 seconds. Registered as a singleton.
/// </summary>
public class ExchangeRateFeed
{
    private readonly Lock _gate = new();
    private int _readings;
    private bool _spikeNext;

    /// <summary>When false, no reading is a spike. Tests use it to hold the feed calm.</summary>
    public bool SpikesEnabled { get; set; } = true;

    /// <summary>Makes the next reading a spike, whatever the cycle.</summary>
    public void SpikeNext()
    {
        lock (_gate)
            _spikeNext = true;
    }

    /// <summary>The change since the previous reading, in percent.</summary>
    public decimal NextChangePercent()
    {
        lock (_gate)
        {
            _readings++;
            var spike = SpikesEnabled && (_spikeNext || _readings % 6 == 0);
            _spikeNext = false;

            return spike ? 7.5m : (_readings % 2 == 0 ? 0.4m : -0.3m);
        }
    }
}
