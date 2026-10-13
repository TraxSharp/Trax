using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate.Resolvers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Attributes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using Trax.Effect.StateMachine.Persistence.Mutations;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>What a typed machine operation answers: the server's snapshot with its revision, or a problem.</summary>
/// <param name="Id">The instance's id; the requested one when the operation was refused.</param>
/// <param name="Revision">The instance's revision after the operation, or null when refused.</param>
/// <param name="State">The state the instance is in, or null when refused.</param>
/// <param name="Context">The instance's context, or null when refused.</param>
/// <param name="Problem">Why the operation was refused, or null when it was not.</param>
internal sealed record MachineAnswer(
    Guid? Id,
    long? Revision,
    string? State,
    JsonObject? Context,
    SnapshotProblem? Problem
)
{
    /// <summary>When the timer of the state the instance is in fires, or null.</summary>
    public DateTimeOffset? DueAt { get; init; }

    /// <summary>
    /// False when the answer replays a request the instance has moved past, whose due time the server cannot know.
    /// </summary>
    public bool DueAtKnown { get; init; } = true;

    /// <summary>A refusal of the operation on <paramref name="id"/>.</summary>
    public static MachineAnswer Refused(Guid? id, string code, string message) =>
        new(id, null, null, null, new SnapshotProblem { Code = code, Message = message });

    /// <summary>A refusal of the operation on <paramref name="id"/>.</summary>
    public static MachineAnswer Refused(Guid? id, SnapshotProblem problem) =>
        new(id, null, null, null, problem);

    /// <summary>The answer holding the canonical snapshot <paramref name="wire"/> the server stored.</summary>
    public static MachineAnswer Of(
        Guid? id,
        string wire,
        long? revision,
        DateTimeOffset? dueAt,
        bool dueAtKnown = true
    )
    {
        var snapshot = JsonNode.Parse(wire) as JsonObject;
        return new MachineAnswer(
            id,
            revision,
            snapshot?["state"]?.GetValue<string>(),
            snapshot?["context"] as JsonObject ?? [],
            null
        )
        {
            DueAt = dueAt,
            DueAtKnown = dueAtKnown,
        };
    }
}

/// <summary>What <c>discard</c> answers.</summary>
/// <param name="Id">The instance's id.</param>
/// <param name="Discarded">Whether the instance is gone.</param>
/// <param name="Problem">Why the discard was refused, or null.</param>
internal sealed record MachineDiscardAnswer(Guid Id, bool Discarded, SnapshotProblem? Problem);

/// <summary>
/// Serialises the reads of one request: <c>get</c> reads through the request's data context, which serves one
/// operation at a time, and HotChocolate resolves two aliased reads at once.
/// </summary>
internal sealed class MachineReadGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Runs <paramref name="read"/> once no other read of this request is running.</summary>
    public async Task<T> RunAsync<T>(Func<Task<T>> read, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await read();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}

/// <summary>
/// The resolvers of the <c>machines</c> root. Every write dispatches the internal state-machine train it stands
/// for (<c>IStartSnapshot</c>, <c>IAdvanceSnapshot</c>, <c>ISendSnapshot</c>, <c>IDiscardSnapshot</c>) through the
/// mediator, as the generic mutations do, so a typed event is authorized, recorded, audited and mapped on the one path
/// every surface shares; the train builds the request's <see cref="Actor.Client"/> and the machine's rules run below
/// it. A refusal comes back as a problem; a failure the server did not expect is logged under a reference and answered
/// with a fixed message, never its own text. <c>get</c> reads through the machine's draft service, checking its read
/// rule against the request's caller, one read of a request at a time.
/// </summary>
internal static class MachineResolvers
{
    private const string LoggerCategory = "Trax.Api.GraphQL.Machines";

    public static async Task<MachineAnswer> StartAsync(
        IResolverContext ctx,
        IMachine machine,
        Type? startType
    )
    {
        var requestId = ctx.ArgumentValue<string>("requestId");
        if (RequestIdProblem(requestId) is { } invalid)
            return MachineAnswer.Refused(null, invalid);

        var (input, sensitive) = InputJson(ctx, startType);
        if (TooLarge(input, sensitive) is { } large)
            return MachineAnswer.Refused(null, large);

        var (output, failure) = await RunAsync<StartSnapshotOutput>(
            ctx,
            typeof(IStartSnapshot),
            new StartSnapshotInput
            {
                Machine = machine.Name,
                Input = input,
                SensitiveInput = sensitive,
                RequestId = requestId,
            },
            machine.Name,
            null
        );
        return output switch
        {
            { Problem: { } problem } => MachineAnswer.Refused(null, problem),
            { Snapshot: { } wire } => MachineAnswer.Of(
                output.Id,
                wire,
                output.Revision,
                output.DueAt
            ),
            _ => Failed(null, failure),
        };
    }

    public static async Task<MachineAnswer> EventAsync(
        IResolverContext ctx,
        IMachine machine,
        MachineTrigger trigger
    )
    {
        var id = ctx.ArgumentValue<Guid>("id");
        var requestId = ctx.ArgumentValue<string>("requestId");
        if (RequestIdProblem(requestId) is { } invalid)
            return MachineAnswer.Refused(id, invalid);

        if (trigger.Kind == TriggerKind.EffectBound)
        {
            // The server runs the effect and hands the edge its receipt: the client sends neither.
            var (sent, failure) = await RunAsync<SendSnapshotOutput>(
                ctx,
                typeof(ISendSnapshot),
                new SendSnapshotInput
                {
                    Machine = machine.Name,
                    Id = id,
                    RequestId = requestId,
                },
                machine.Name,
                id
            );
            return sent switch
            {
                { Problem: { } problem } => MachineAnswer.Refused(id, problem),
                { Snapshot: { } wire } => MachineAnswer.Of(
                    id,
                    wire,
                    sent.Revision,
                    sent.DueAt,
                    sent.DueAtKnown
                ),
                _ => Failed(id, failure),
            };
        }

        var (input, sensitive) = InputJson(
            ctx,
            trigger.InputType is { } type && MachineSurface.HasFields(type) ? type : null
        );
        if (TooLarge(input, sensitive) is { } large)
            return MachineAnswer.Refused(id, large);

        var clientResult = ctx.ArgumentValue<string?>("clientResult");
        if (
            clientResult is not null
            && Encoding.UTF8.GetByteCount(clientResult) > SnapshotLimits.MaxSnapshotBytes
        )
            return MachineAnswer.Refused(
                id,
                RejectionReasons.TooLarge,
                $"The client result exceeds the {SnapshotLimits.MaxSnapshotBytes}-byte limit."
            );

        var (advanced, failed) = await RunAsync<AdvanceSnapshotOutput>(
            ctx,
            typeof(IAdvanceSnapshot),
            new AdvanceSnapshotInput
            {
                Machine = machine.Name,
                Id = id,
                Trigger = trigger.Name,
                Input = input,
                SensitiveInput = sensitive,
                RequestId = requestId,
                ClientResult = clientResult,
            },
            machine.Name,
            id
        );
        return advanced switch
        {
            { Problem: { } problem } => MachineAnswer.Refused(id, problem),
            { Snapshot: { } wire } => MachineAnswer.Of(
                id,
                wire,
                advanced.Revision,
                advanced.DueAt,
                advanced.DueAtKnown
            ),
            _ => Failed(id, failed),
        };
    }

    public static async Task<MachineDiscardAnswer> DiscardAsync(
        IResolverContext ctx,
        IMachine machine
    )
    {
        var id = ctx.ArgumentValue<Guid>("id");
        var requestId = ctx.ArgumentValue<string>("requestId");
        if (RequestIdProblem(requestId) is { } invalid)
            return new MachineDiscardAnswer(id, false, invalid);

        var (output, failure) = await RunAsync<DiscardSnapshotOutput>(
            ctx,
            typeof(IDiscardSnapshot),
            new DiscardSnapshotInput
            {
                Machine = machine.Name,
                Id = id,
                RequestId = requestId,
            },
            machine.Name,
            id
        );
        return output is null
            ? new MachineDiscardAnswer(id, false, Failed(id, failure).Problem)
            : new MachineDiscardAnswer(id, output.Discarded, output.Problem);
    }

    public static async Task<MachineAnswer?> GetAsync(IResolverContext ctx, IMachine machine)
    {
        var id = ctx.ArgumentValue<Guid>("id");
        var principal = ctx.Services.GetService<ISnapshotPrincipal>();
        if (principal?.CurrentUserKey is not { } userKey || string.IsNullOrWhiteSpace(userKey))
            return null;

        var caller = ctx.Services.GetService<ICallerAuthorization>();
        Actor actor = caller is null
            ? new Actor.Client(userKey)
            : new Actor.Client(userKey, caller.IsInRole, caller.SatisfiesPolicy);
        var gate = ctx.Services.GetRequiredService<MachineReadGate>();

        try
        {
            return await gate.RunAsync(
                async () =>
                {
                    var service = ctx
                        .Services.GetRequiredService<ISnapshotMachineRegistry>()
                        .Service(machine.Name);
                    if (service is null)
                        return null;

                    return await service.Load(actor, id, ctx.RequestAborted) switch
                    {
                        LoadResult.Loaded loaded => new MachineAnswer(
                            id,
                            loaded.Revision,
                            loaded.Snapshot.State,
                            loaded.Snapshot.Context,
                            null
                        )
                        {
                            DueAt = loaded.DueAt,
                        },
                        LoadResult.Invalid invalid => MachineAnswer.Refused(
                            id,
                            invalid.Code,
                            "The stored instance cannot be read by this version of the machine."
                        ),
                        _ => null,
                    };
                },
                ctx.RequestAborted
            );
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException
                || !ctx.RequestAborted.IsCancellationRequested
            )
        {
            return Failed(ctx, ex, machine.Name, id);
        }
    }

    // A train run through the mediator: authorized, recorded and audited as the generic mutations are. A caller the
    // train's authorization refuses is forbidden; any other failure is logged under a reference and answered with a
    // fixed problem; a cancelled request stays cancelled.
    private static async Task<(TOutput? Output, SnapshotProblem? Failure)> RunAsync<TOutput>(
        IResolverContext ctx,
        Type train,
        object input,
        string machine,
        Guid? id
    )
        where TOutput : class
    {
        try
        {
            var result = await ctx
                .Services.GetRequiredService<ITrainExecutionService>()
                .RunAsync(
                    train.FullName!,
                    TrainInputReader.Write(input, input.GetType()),
                    ctx.RequestAborted
                );
            return (
                result.Output as TOutput
                    ?? throw new InvalidOperationException(
                        $"{train.Name} answered {result.Output?.GetType().Name ?? "nothing"}, not {typeof(TOutput).Name}."
                    ),
                null
            );
        }
        catch (UnauthorizedAccessException)
        {
            return (
                null,
                new SnapshotProblem
                {
                    Code = DraftRefusalCodes.Forbidden,
                    Message = "You may not do this.",
                }
            );
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException
                || !ctx.RequestAborted.IsCancellationRequested
            )
        {
            return (null, Unexpected(Reference(ctx, ex, machine, id)));
        }
    }

    // The answer to an operation whose train answered nothing usable: the problem RunAsync built, or a fixed one.
    private static MachineAnswer Failed(Guid? id, SnapshotProblem? failure) =>
        MachineAnswer.Refused(id, failure ?? Unexpected(null));

    private static SnapshotProblem Unexpected(string? reference) =>
        new() { Code = RejectionReasons.InternalError, Message = FailedMessage(reference) };

    private static MachineAnswer Failed(
        IResolverContext ctx,
        Exception ex,
        string machine,
        Guid? id
    ) =>
        MachineAnswer.Refused(
            id,
            RejectionReasons.InternalError,
            FailedMessage(Reference(ctx, ex, machine, id))
        );

    private static string FailedMessage(string? reference) =>
        "The request could not be completed. Try again."
        + (reference is null ? "" : $" Reference: {reference}.");

    private static string Reference(IResolverContext ctx, Exception ex, string machine, Guid? id)
    {
        var reference = Guid.NewGuid().ToString("N");
        ctx.Services.GetService<ILoggerFactory>()
            ?.CreateLogger(LoggerCategory)
            .LogError(
                ex,
                "The {Machine} operation on {DraftId} failed. Reference {Reference}.",
                machine,
                id,
                reference
            );
        return reference;
    }

    // The draft service's own rule, checked here too so a refused id never reaches the train.
    private static SnapshotProblem? RequestIdProblem(string? requestId) =>
        RequestIdRing.IsUsable(requestId)
            ? null
            : new SnapshotProblem
            {
                Code = DraftRefusalCodes.InvalidRequestId,
                Message =
                    $"A request id is 1 to {RequestIdRing.MaxRequestIdLength} characters, not blank, with no control character.",
            };

    private static SnapshotProblem? TooLarge(string? input, string? sensitive) =>
        (input is null ? 0 : Encoding.UTF8.GetByteCount(input))
            + (sensitive is null ? 0 : Encoding.UTF8.GetByteCount(sensitive))
        > SnapshotLimits.MaxSnapshotBytes
            ? new SnapshotProblem
            {
                Code = RejectionReasons.TooLarge,
                Message = $"The input exceeds the {SnapshotLimits.MaxSnapshotBytes}-byte limit.",
            }
            : null;

    /// <summary>
    /// The <c>input</c> argument as the JSON the machine validates, split in two: the fields its record marks
    /// <c>[TraxSensitive]</c> (or that reach a marked member) travel apart, so every recorded copy of the run's input
    /// masks them. Both null when the operation takes no input.
    /// </summary>
    private static (string? Input, string? Sensitive) InputJson(IResolverContext ctx, Type? type)
    {
        if (type is null)
            return (null, null);
        var value = ctx.ArgumentValue<object?>("input");
        if (value is null)
            return (null, null);

        var json = JsonSerializer.SerializeToNode(value, type, MachineSurface.Json) as JsonObject;
        if (json is null)
            return (null, null);

        var sensitive = new JsonObject();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!IsSensitive(type, property))
                continue;
            var name = MachineSurface.JsonName(property);
            if (!json.Remove(name, out var field))
                continue;
            sensitive[name] = field;
        }

        return (
            json.ToJsonString(MachineSurface.Json),
            sensitive.Count == 0 ? null : sensitive.ToJsonString(MachineSurface.Json)
        );
    }

    private static bool IsSensitive(Type type, PropertyInfo property) =>
        property.IsDefined(typeof(TraxSensitiveAttribute), inherit: true)
        || type.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Any(p => p.Name == property.Name && p.IsDefined(typeof(TraxSensitiveAttribute), false))
        || TraxRedaction.ReachesSensitiveMember(property.PropertyType);
}
