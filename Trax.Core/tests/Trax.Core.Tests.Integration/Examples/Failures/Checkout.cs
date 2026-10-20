using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;

namespace Trax.Core.Tests.Integration.Examples.Failures;

// Shown on https://traxsharp.net/docs/cross-cutting/handle-errors-in-trains
// Shown on https://traxsharp.net/docs/sdk-reference/trains-and-junctions/train
//
// A checkout whose card is always declined. The tests beside this file show what a failing
// junction does to the rest of the train, and what the caller gets back.

public sealed record Order(decimal Total);

public sealed record Payment(decimal Amount);

public sealed record Receipt(decimal Amount);

public interface IOutbox
{
    List<string> Sent { get; }
}

public sealed class Outbox : IOutbox
{
    public List<string> Sent { get; } = [];
}

public class ChargeCard : Junction<Order, Payment>
{
    public override Task<Payment> Run(Order order) =>
        throw new InvalidOperationException("Card declined");
}

public class SendReceipt(IOutbox outbox) : Junction<Payment, Receipt>
{
    public override Task<Receipt> Run(Payment payment)
    {
        outbox.Sent.Add($"receipt for {payment.Amount}");
        return Task.FromResult(new Receipt(payment.Amount));
    }
}

public class CheckoutTrain(IOutbox outbox) : Train<Order, Receipt>
{
    protected override Task<Either<Exception, Receipt>> Junctions() =>
        AddServices(outbox).Chain<ChargeCard>().Chain<SendReceipt>().Resolve();
}
