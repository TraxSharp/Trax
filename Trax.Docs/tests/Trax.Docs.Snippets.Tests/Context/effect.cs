// What the compiled example on effect.md takes as given.
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

public record CreateUserRequest(string Email);

public record User(string Email);

public interface ICreateUserTrain : IServiceTrain<CreateUserRequest, User>;

public class ValidateUserJunction : Junction<CreateUserRequest, CreateUserRequest>
{
    public override Task<CreateUserRequest> Run(CreateUserRequest input) => Task.FromResult(input);
}

public class CreateUserJunction : Junction<CreateUserRequest, User>
{
    public override Task<User> Run(CreateUserRequest input) =>
        Task.FromResult(new User(input.Email));
}
