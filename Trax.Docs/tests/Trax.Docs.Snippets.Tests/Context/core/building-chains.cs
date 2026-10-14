// What the examples on core/building-chains.md take as given: the requests, results and junctions
// the example trains chain.
using Trax.Core.Functional;
using Trax.Core.Junction;

public record EmailAddress(string Value);

public record UserId(string Value);

public record CreateUserRequest(string Name, EmailAddress Email);

public record User(string Name, EmailAddress Email);

public record OrderRequest(string OrderId);

public record Price(decimal Amount);

public record Payment(string Reference);

public record OrderResult(string OrderId, string PaymentReference);

public interface IValidator
{
    bool IsValid(CreateUserRequest request);
}

public interface INotifier
{
    Task Notify(User user);
}

public class ValidateEmailJunction : Junction<CreateUserRequest, Unit>
{
    public override Task<Unit> Run(CreateUserRequest input) => Task.FromResult(Unit.Default);
}

public class CreateUserJunction : Junction<CreateUserRequest, User>
{
    public override Task<User> Run(CreateUserRequest input) =>
        Task.FromResult(new User(input.Name, input.Email));
}

public class SendEmailJunction : Junction<User, Unit>
{
    public override Task<Unit> Run(User input) => Task.FromResult(Unit.Default);
}

public class LoadUserJunction : Junction<UserId, User>
{
    public override Task<User> Run(UserId input) =>
        Task.FromResult(new User(input.Value, new EmailAddress($"{input.Value}@example.com")));
}

public class VerifyEmailAddressJunction : Junction<EmailAddress, Unit>
{
    public override Task<Unit> Run(EmailAddress input) => Task.FromResult(Unit.Default);
}

public class ValidateJunction : Junction<(CreateUserRequest, IValidator), Unit>
{
    public override Task<Unit> Run((CreateUserRequest, IValidator) input) =>
        Task.FromResult(Unit.Default);
}

public class NotifyJunction : Junction<(User, INotifier), Unit>
{
    public override async Task<Unit> Run((User, INotifier) input)
    {
        await input.Item2.Notify(input.Item1);
        return Unit.Default;
    }
}

public class ValidateOrderJunction : Junction<OrderRequest, Unit>
{
    public override Task<Unit> Run(OrderRequest input) => Task.FromResult(Unit.Default);
}

public class CheckCacheJunction : Junction<OrderRequest, OrderResult>
{
    public override Task<OrderResult> Run(OrderRequest input) =>
        throw new KeyNotFoundException("Not cached.");
}

public class CalculatePricingJunction : Junction<OrderRequest, Price>
{
    public override Task<Price> Run(OrderRequest input) => Task.FromResult(new Price(10m));
}

public class ProcessPaymentJunction : Junction<Price, Payment>
{
    public override Task<Payment> Run(Price input) => Task.FromResult(new Payment("pay_1"));
}

public class SaveOrderJunction : Junction<(OrderRequest, Payment), OrderResult>
{
    public override Task<OrderResult> Run((OrderRequest, Payment) input) =>
        Task.FromResult(new OrderResult(input.Item1.OrderId, input.Item2.Reference));
}
