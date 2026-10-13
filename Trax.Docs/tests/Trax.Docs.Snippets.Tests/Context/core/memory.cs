// What the examples on core/memory.md take as given: the entities a checkout loads, the services
// that load and process them, and the junctions that validate them.
using Trax.Core.Functional;
using Trax.Core.Junction;

public class User
{
    public string? EnrichedData { get; set; }
}

public record Order(string OrderId);

public record Payment(string PaymentId);

public record Receipt(string Number);

public record LoadRequest(string UserId, string OrderId, string PaymentId);

public interface IRepository
{
    Task<User> GetUserAsync(string id);
    Task<Order> GetOrderAsync(string id);
    Task<Payment> GetPaymentAsync(string id);
}

public interface ICheckout
{
    Task<Receipt> ProcessAsync(User user, Order order, Payment payment);
}

public class ValidateUserJunction : Junction<User, Unit>
{
    public override Task<Unit> Run(User input) => Task.FromResult(Unit.Default);
}

public class ValidateOrderJunction : Junction<Order, Unit>
{
    public override Task<Unit> Run(Order input) => Task.FromResult(Unit.Default);
}
