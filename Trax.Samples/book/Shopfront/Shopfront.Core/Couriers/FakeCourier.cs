using System.Collections.Concurrent;

namespace Shopfront.Couriers;

// Takes two seconds to quote, and remembers every label it sold while the process runs.
public class FakeCourier : ICourier
{
    private readonly ConcurrentDictionary<Guid, string> labels = new();

    public TimeSpan QuoteTime { get; init; } = TimeSpan.FromSeconds(2);

    public async Task<CourierRates> Rates(CancellationToken cancellationToken)
    {
        await Task.Delay(QuoteTime, cancellationToken);
        return new CourierRates(Base: 4.00m, PerKilogram: 1.50m);
    }

    public Task<string> BuyLabel(
        Guid orderId,
        decimal price,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(
            labels.GetOrAdd(orderId, id => $"TRK-{id.ToString("N")[..8].ToUpperInvariant()}")
        );
}
