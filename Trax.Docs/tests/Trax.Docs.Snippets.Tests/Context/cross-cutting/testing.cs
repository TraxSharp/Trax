// What the compiled example on cross-cutting/testing.md takes as given: the application's own
// user model and repository interface, which the fake implements.
public record User
{
    public int Id { get; init; }

    public string Email { get; init; } = "";
}

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email);

    Task<User> CreateAsync(User user);
}
