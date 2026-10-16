using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.StateMachine.E2E;

// A trivial anonymous query train. HotChocolate requires a non-empty root Query type, and the six
// stateMachine trains are all mutations, so a host that exposes only them needs at least one query. A real
// host has its own queries (or calls ExposeOperationQueries() with a scheduler); the E2E supplies this one
// so the schema builds without dragging in the scheduler.

public record PingInput
{
    public string? Value { get; init; }
}

public record PingOutput
{
    public string? Value { get; init; }
}

public interface IPing : IServiceTrain<PingInput, PingOutput> { }

[TraxAllowAnonymous]
[TraxQuery(Description = "Liveness echo; keeps the root Query type non-empty.")]
public class Ping : ServiceTrain<PingInput, PingOutput>, IPing
{
    protected override Task<Either<Exception, PingOutput>> Junctions() =>
        Chain<PingJunction>().Resolve();
}

public class PingJunction : Junction<PingInput, PingOutput>
{
    public override Task<PingOutput> Run(PingInput input) =>
        Task.FromResult(new PingOutput { Value = input.Value });
}

// A mutation train only an approver may run, so a host's train documents include one that is role-gated.

public record RecordNoteInput
{
    public string Text { get; init; } = "";
}

public record RecordNoteOutput
{
    public string Text { get; init; } = "";
}

public interface IRecordNote : IServiceTrain<RecordNoteInput, RecordNoteOutput> { }

[TraxAuthorize(Roles = "Approver")]
[TraxMutation(Description = "Echoes a note; only an approver may run it.")]
public class RecordNote : ServiceTrain<RecordNoteInput, RecordNoteOutput>, IRecordNote
{
    protected override Task<Either<Exception, RecordNoteOutput>> Junctions() =>
        Chain<RecordNoteJunction>().Resolve();
}

public class RecordNoteJunction : Junction<RecordNoteInput, RecordNoteOutput>
{
    public override Task<RecordNoteOutput> Run(RecordNoteInput input) =>
        Task.FromResult(new RecordNoteOutput { Text = input.Text });
}

// A query train answering with an entity graph that refers back to itself, as an EF model with navigations both ways
// does: the order's customer lists the customer's orders.

public record FindOrderInput
{
    public int Id { get; init; }
}

public record OrderOutput
{
    public string Title { get; init; } = "";
    public CustomerOutput Customer { get; init; } = new();
}

public record CustomerOutput
{
    public string Name { get; init; } = "";
    public OrderOutput[] Orders { get; init; } = [];
}

public interface IFindOrder : IServiceTrain<FindOrderInput, OrderOutput> { }

[TraxAllowAnonymous]
[TraxQuery(Description = "An order and its customer, whose orders lead back to the order.")]
public class FindOrder : ServiceTrain<FindOrderInput, OrderOutput>, IFindOrder
{
    protected override Task<Either<Exception, OrderOutput>> Junctions() =>
        Chain<FindOrderJunction>().Resolve();
}

public class FindOrderJunction : Junction<FindOrderInput, OrderOutput>
{
    public override Task<OrderOutput> Run(FindOrderInput input)
    {
        var order = new OrderOutput { Title = $"Order {input.Id}" };
        var customer = new CustomerOutput { Name = "Ada", Orders = [order] };
        return Task.FromResult(order with { Customer = customer });
    }
}

// A query train answering seven types that each list all six others, as an EF model whose entities navigate to each
// other does. Selecting everything such an answer reaches grows with the number of paths through it (7,828 fields for
// these seven), so no host may try: every host in this suite exposes it, and a client document reaches it.

public record ReadMeshInput
{
    public string Name { get; init; } = "";
}

public record Mesh0Output
{
    public string Name { get; init; } = "";
    public Mesh1Output[] Mesh1s { get; init; } = [];
    public Mesh2Output[] Mesh2s { get; init; } = [];
    public Mesh3Output[] Mesh3s { get; init; } = [];
    public Mesh4Output[] Mesh4s { get; init; } = [];
    public Mesh5Output[] Mesh5s { get; init; } = [];
    public Mesh6Output[] Mesh6s { get; init; } = [];
}

public record Mesh1Output
{
    public string Name { get; init; } = "";
    public Mesh0Output[] Mesh0s { get; init; } = [];
    public Mesh2Output[] Mesh2s { get; init; } = [];
    public Mesh3Output[] Mesh3s { get; init; } = [];
    public Mesh4Output[] Mesh4s { get; init; } = [];
    public Mesh5Output[] Mesh5s { get; init; } = [];
    public Mesh6Output[] Mesh6s { get; init; } = [];
}

public record Mesh2Output
{
    public string Name { get; init; } = "";
    public Mesh0Output[] Mesh0s { get; init; } = [];
    public Mesh1Output[] Mesh1s { get; init; } = [];
    public Mesh3Output[] Mesh3s { get; init; } = [];
    public Mesh4Output[] Mesh4s { get; init; } = [];
    public Mesh5Output[] Mesh5s { get; init; } = [];
    public Mesh6Output[] Mesh6s { get; init; } = [];
}

public record Mesh3Output
{
    public string Name { get; init; } = "";
    public Mesh0Output[] Mesh0s { get; init; } = [];
    public Mesh1Output[] Mesh1s { get; init; } = [];
    public Mesh2Output[] Mesh2s { get; init; } = [];
    public Mesh4Output[] Mesh4s { get; init; } = [];
    public Mesh5Output[] Mesh5s { get; init; } = [];
    public Mesh6Output[] Mesh6s { get; init; } = [];
}

public record Mesh4Output
{
    public string Name { get; init; } = "";
    public Mesh0Output[] Mesh0s { get; init; } = [];
    public Mesh1Output[] Mesh1s { get; init; } = [];
    public Mesh2Output[] Mesh2s { get; init; } = [];
    public Mesh3Output[] Mesh3s { get; init; } = [];
    public Mesh5Output[] Mesh5s { get; init; } = [];
    public Mesh6Output[] Mesh6s { get; init; } = [];
}

public record Mesh5Output
{
    public string Name { get; init; } = "";
    public Mesh0Output[] Mesh0s { get; init; } = [];
    public Mesh1Output[] Mesh1s { get; init; } = [];
    public Mesh2Output[] Mesh2s { get; init; } = [];
    public Mesh3Output[] Mesh3s { get; init; } = [];
    public Mesh4Output[] Mesh4s { get; init; } = [];
    public Mesh6Output[] Mesh6s { get; init; } = [];
}

public record Mesh6Output
{
    public string Name { get; init; } = "";
    public Mesh0Output[] Mesh0s { get; init; } = [];
    public Mesh1Output[] Mesh1s { get; init; } = [];
    public Mesh2Output[] Mesh2s { get; init; } = [];
    public Mesh3Output[] Mesh3s { get; init; } = [];
    public Mesh4Output[] Mesh4s { get; init; } = [];
    public Mesh5Output[] Mesh5s { get; init; } = [];
}

public interface IReadMesh : IServiceTrain<ReadMeshInput, Mesh0Output> { }

[TraxAllowAnonymous]
[TraxQuery(Description = "Seven types that each list the six others.")]
public class ReadMesh : ServiceTrain<ReadMeshInput, Mesh0Output>, IReadMesh
{
    protected override Task<Either<Exception, Mesh0Output>> Junctions() =>
        Chain<ReadMeshJunction>().Resolve();
}

public class ReadMeshJunction : Junction<ReadMeshInput, Mesh0Output>
{
    public override Task<Mesh0Output> Run(ReadMeshInput input) =>
        Task.FromResult(
            new Mesh0Output
            {
                Name = input.Name,
                Mesh1s =
                [
                    new Mesh1Output { Name = "one", Mesh2s = [new Mesh2Output { Name = "two" }] },
                ],
            }
        );
}
