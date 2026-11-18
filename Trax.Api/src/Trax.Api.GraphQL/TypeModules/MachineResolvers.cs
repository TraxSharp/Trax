using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate.Resolvers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Extensions;
using Trax.Effect.Attributes;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainExecution;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using Trax.StateMachine.Persistence.Mutations;

namespace Trax.Api.GraphQL.TypeModules;

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
            return MachineAnswer.Refused(id: null, invalid);

        var (input, sensitive) = InputJson(ctx, startType);
        if (TooLarge(input, sensitive) is { } large)
            return MachineAnswer.Refused(id: null, large);

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
                id: null
            )
            .ConfigureAwait(false);
        return output switch
        {
            { Problem: { } problem } => MachineAnswer.Refused(id: null, problem),
            { Snapshot: { } wire } => MachineAnswer.Of(
                output.Id,
                wire,
                output.Revision,
                output.DueAt
            ),
            _ => Failed(id: null, failure),
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
                )
                .ConfigureAwait(false);
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
                RefusalCode.TooLarge,
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
            )
            .ConfigureAwait(false);
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
            return new MachineDiscardAnswer(id, Discarded: false, invalid);

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
            )
            .ConfigureAwait(false);
        return output is null
            ? new MachineDiscardAnswer(id, Discarded: false, Failed(id, failure).Problem)
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

                        return await service
                            .Load(actor, id, ctx.RequestAborted)
                            .ConfigureAwait(false) switch
                        {
                            LoadResult.Loaded loaded => new MachineAnswer(
                                id,
                                loaded.Revision,
                                loaded.Snapshot.State,
                                loaded.Snapshot.Context,
                                Problem: null
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
                )
                .ConfigureAwait(false);
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
                    train.FullName.NotNull(),
                    TrainInputReader.Write(input, input.GetType()),
                    ctx.RequestAborted
                )
                .ConfigureAwait(false);
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
                    Code = RefusalCode.Forbidden,
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
        MachineAnswer.Refused(id, failure ?? Unexpected(reference: null));

    private static SnapshotProblem Unexpected(string? reference) =>
        new() { Code = RefusalCode.InternalError, Message = FailedMessage(reference) };

    private static MachineAnswer Failed(
        IResolverContext ctx,
        Exception ex,
        string machine,
        Guid? id
    ) =>
        MachineAnswer.Refused(
            id,
            RefusalCode.InternalError,
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
                Code = RefusalCode.InvalidRequestId,
                Message =
                    $"A request id is 1 to {RequestIdRing.MaxRequestIdLength} characters, not blank, with no control character.",
            };

    private static SnapshotProblem? TooLarge(string? input, string? sensitive) =>
        (input is null ? 0 : Encoding.UTF8.GetByteCount(input))
            + (sensitive is null ? 0 : Encoding.UTF8.GetByteCount(sensitive))
        > SnapshotLimits.MaxSnapshotBytes
            ? new SnapshotProblem
            {
                Code = RefusalCode.TooLarge,
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
            .Any(p =>
                string.Equals(p.Name, property.Name, StringComparison.Ordinal)
                && p.IsDefined(typeof(TraxSensitiveAttribute), inherit: false)
            )
        || TraxRedaction.ReachesSensitiveMember(property.PropertyType);
}
