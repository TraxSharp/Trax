// What the compiled examples on mediator.md take as given: a user train and its types.
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

public record CreateUserRequest(string Email);

public record User(int Id, string Email);

public interface ICreateUserTrain : IServiceTrain<CreateUserRequest, User>;

public class CreateUserTrain : ServiceTrain<CreateUserRequest, User>, ICreateUserTrain
{
    protected override Task<Either<Exception, User>> Junctions() =>
        Chain<CreateUserJunction>().Resolve();
}

public class CreateUserJunction : Junction<CreateUserRequest, User>
{
    public override Task<User> Run(CreateUserRequest input) =>
        Task.FromResult(new User(1, input.Email));
}
