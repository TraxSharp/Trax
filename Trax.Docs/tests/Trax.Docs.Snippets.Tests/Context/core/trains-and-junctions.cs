// What the compiled examples on core/trains-and-junctions.md take as given.
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

public record CreateUserRequest(string Email, string FirstName, string LastName);

public record User(string Email, string FullName);

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email);

    Task<User> CreateAsync(User user);
}

public interface ISlackClient
{
    Task PostAsync(string message, CancellationToken ct);
}

public interface ICreateUserTrain : IServiceTrain<CreateUserRequest, User>;

public class CreateUserJunction(IUserRepository users) : Junction<CreateUserRequest, User>
{
    public override Task<User> Run(CreateUserRequest input) =>
        users.CreateAsync(new User(input.Email, $"{input.FirstName} {input.LastName}"));
}
