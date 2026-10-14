// What the examples on core.md take as given: the request, the user and the three junctions the
// example train chains.
using Trax.Core.Functional;
using Trax.Core.Junction;

public record CreateUserRequest(string Email);

public record User(string Email);

public class ValidateUserJunction : Junction<CreateUserRequest, Unit>
{
    public override Task<Unit> Run(CreateUserRequest input) => Task.FromResult(Unit.Default);
}

public class CreateUserJunction : Junction<CreateUserRequest, User>
{
    public override Task<User> Run(CreateUserRequest input) =>
        Task.FromResult(new User(input.Email));
}

public class SendEmailJunction : Junction<User, Unit>
{
    public override Task<Unit> Run(User input) => Task.FromResult(Unit.Default);
}
