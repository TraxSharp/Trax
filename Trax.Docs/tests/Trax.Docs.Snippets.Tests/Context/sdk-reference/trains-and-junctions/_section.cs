// What the examples under sdk-reference/trains-and-junctions take as given: the order domain they
// share. Junctions and trains a page declares itself are not here; the ones a page only names are
// in that page's own context file.
using Trax.Core.Junction;

public record CreateOrderInput(string CustomerId, decimal Total);

public record ValidatedOrder(string CustomerId, decimal Total);

public record PaymentReceipt(string CustomerId, string ChargeId);

public record OrderResult(long Id);

public record StoredOrder(long Id);

public interface IPaymentGateway
{
    Task<PaymentReceipt> Charge(string customerId, decimal total, CancellationToken ct);
}

public interface IOrderStore
{
    Task<StoredOrder> Save(PaymentReceipt receipt, CancellationToken ct);
}

public record ScoreInput(int Value);

public record NormalizedScore(double Value);

public record ScoreResult(string Rank);

public class NormalizeScore : Junction<ScoreInput, NormalizedScore>
{
    public override Task<NormalizedScore> Run(ScoreInput input) =>
        Task.FromResult(new NormalizedScore(input.Value / 100.0));
}

public class RankScore : Junction<NormalizedScore, ScoreResult>
{
    public override Task<ScoreResult> Run(NormalizedScore input) =>
        Task.FromResult(new ScoreResult(input.Value >= 0.5 ? "high" : "low"));
}
