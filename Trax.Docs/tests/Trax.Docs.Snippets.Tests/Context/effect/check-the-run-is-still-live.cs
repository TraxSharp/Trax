// What the compiled example on effect/check-the-run-is-still-live.md takes as given.
public record PatchCustomer(string CustomerId, string Email);

public interface ICustomerApi
{
    Task SendAsync(PatchCustomer patch);
}
