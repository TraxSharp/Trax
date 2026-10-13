// What the compiled examples on core/organize-trains.md take as given.
public record User(string Email, string FullName);

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email);

    Task<User> CreateAsync(User user);
}
