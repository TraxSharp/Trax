// What the examples on cross-cutting/functional-programming.md take as given: the request, the
// user and the junction that creates it.
using Trax.Core.Junction;

public record CreateUserRequest(string Email);

public record User(string Email);

public class CreateUserJunction : Junction<CreateUserRequest, User>
{
    public override Task<User> Run(CreateUserRequest input) =>
        Task.FromResult(new User(input.Email));
}
