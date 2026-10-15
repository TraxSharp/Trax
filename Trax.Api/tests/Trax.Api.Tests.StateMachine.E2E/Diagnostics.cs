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
