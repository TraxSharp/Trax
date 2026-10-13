// What the compiled example on cross-cutting/test-a-train.md takes as given: a train with one
// junction, the repository it uses, and the fake the test registers for it.
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

public record User
{
    public int Id { get; init; }

    public string Email { get; init; } = "";
}

public record CreateUserRequest
{
    public required string Email { get; init; }
}

public interface IUserRepository
{
    Task<User> CreateAsync(User user);
}

public class FakeUserRepository : IUserRepository
{
    public Task<User> CreateAsync(User user) => Task.FromResult(user with { Id = 1 });
}

public interface ICreateUserTrain : IServiceTrain<CreateUserRequest, User>;

public class CreateUserTrain : ServiceTrain<CreateUserRequest, User>, ICreateUserTrain
{
    protected override Task<Either<Exception, User>> Junctions() =>
        Chain<CreateUserJunction>().Resolve();
}

public class CreateUserJunction(IUserRepository repository) : Junction<CreateUserRequest, User>
{
    public override Task<User> Run(CreateUserRequest input) =>
        repository.CreateAsync(new User { Email = input.Email });
}
