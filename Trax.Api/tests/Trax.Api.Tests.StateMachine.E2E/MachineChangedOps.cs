using System.Text.Json;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// A machine's push (<c>&lt;machine&gt;Changed</c>) and the operator feed, and the readers of their events. The
/// documents are the contract the push is written to:
/// <code>
/// subscription { wizardChanged(id: "...") { revision state gone reload problem { code message } } }
/// subscription { onMachineInstanceChanged(machine: "wizard") { rowId machine ownerKind id state revision hasLiveInvokedRun } }
/// </code>
/// <para>
/// Each push event is the instance's whole snapshot with its revision; the stream ends after an event
/// with <c>gone</c> set (the instance was deleted or expired) or a <c>problem</c> (<c>CLIENT_OUTDATED</c> and the
/// like). <c>reload</c> is set on an event after which the client must read the instance again, which a skipped
/// revision never causes. The user key is the socket's principal, never an argument. The operator feed carries an
/// instance's state, revision and whether it waits on a run, never its context or its owner's key.
/// </para>
/// </summary>
public static class MachineChangedOps
{
    /// <summary>What a push event selects.</summary>
    public const string EventFields = "revision state gone reload problem { code message }";

    /// <summary>What an operator feed event selects.</summary>
    public const string OperatorFields =
        "rowId machine ownerKind id state revision hasLiveInvokedRun";

    /// <summary>The push of instance <paramref name="id"/> of <paramref name="machine"/>.</summary>
    public static string Subscribe(Guid id, string machine = Fakes.WizardMachine.MachineId) =>
        $"subscription {{ {Field(machine)}(id: {WizardOps.Literal(id.ToString())}) {{ {EventFields} }} }}";

    /// <summary>The push's field for <paramref name="machine"/>: <c>wizard</c> is <c>wizardChanged</c>.</summary>
    public static string Field(string machine = Fakes.WizardMachine.MachineId) =>
        string.Concat(
            machine
                .Split('-')
                .Select((part, i) => i == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..])
        ) + "Changed";

    /// <summary>The operator feed, of one machine or of every machine.</summary>
    public static string SubscribeOperator(string? machine = Fakes.WizardMachine.MachineId) =>
        machine is null
            ? $"subscription {{ onMachineInstanceChanged {{ {OperatorFields} }} }}"
            : $"subscription {{ onMachineInstanceChanged(machine: {WizardOps.Literal(machine)}) {{ {OperatorFields} }} }}";

    /// <summary>
    /// The next push event of operation <paramref name="operation"/>, whatever machine it follows. Throws, quoting the message,
    /// when the operation errors or completes instead.
    /// </summary>
    public static async Task<MachineChange> NextChangeAsync(
        this GraphQLWs ws,
        string operation,
        TimeSpan? timeout = null
    ) => new(Only(await ws.NextDataAsync(operation, timeout)));

    /// <summary>The one field a push's data holds.</summary>
    public static JsonElement Only(JsonElement data) =>
        data.EnumerateObject().Single().Value.Clone();

    /// <summary>
    /// Reads events of <paramref name="operation"/> until one has <paramref name="revision"/> or more, and returns
    /// every event read, in order.
    /// </summary>
    public static async Task<IReadOnlyList<MachineChange>> ReadUntilRevisionAsync(
        this GraphQLWs ws,
        string operation,
        long revision,
        TimeSpan? timeout = null
    )
    {
        // Each event is waited for at most the timeout; the stream is finite, so the loop ends.
        var events = new List<MachineChange>();
        while (true)
        {
            var change = await ws.NextChangeAsync(operation, timeout);
            events.Add(change);
            if (change.Revision >= revision || change.Gone || change.ProblemCode is not null)
                return events;
        }
    }

    /// <summary>Reads events of <paramref name="operation"/> until one has <c>gone</c> or a problem, and returns them.</summary>
    public static async Task<IReadOnlyList<MachineChange>> ReadUntilEndAsync(
        this GraphQLWs ws,
        string operation,
        TimeSpan? timeout = null
    )
    {
        var events = new List<MachineChange>();
        while (true)
        {
            var change = await ws.NextChangeAsync(operation, timeout);
            events.Add(change);
            if (change.Gone || change.ProblemCode is not null)
                return events;
        }
    }

    /// <summary>
    /// The first message of <paramref name="operation"/> with its operation id taken out, so the answers to two
    /// subscriptions can be compared.
    /// </summary>
    public static async Task<string> FirstAnswerAsync(this GraphQLWs ws, string operation)
    {
        var message = await ws.NextAsync(operation);
        var type = message.GetProperty("type").GetString();
        var payload = message.TryGetProperty("payload", out var p) ? p.GetRawText() : "";
        return $"{type}:{payload}";
    }

    /// <summary>
    /// Whether the first message of <paramref name="operation"/> is a refusal: an <c>error</c>, a <c>next</c> carrying
    /// errors and no snapshot, or an event carrying a problem. Returns the codes the refusal carries.
    /// </summary>
    public static async Task<(bool Refused, IReadOnlyList<string> Codes, string Raw)> RefusalAsync(
        this GraphQLWs ws,
        string operation
    )
    {
        var message = await ws.NextAsync(operation);
        var raw = message.GetRawText();
        var type = message.GetProperty("type").GetString();
        var codes = new List<string>();
        if (type == "error")
        {
            foreach (var error in message.GetProperty("payload").EnumerateArray())
                if (
                    error.TryGetProperty("extensions", out var ext)
                    && ext.TryGetProperty("code", out var code)
                    && code.GetString() is { } text
                )
                    codes.Add(text);
            return (true, codes, raw);
        }
        if (type != "next")
            return (type == "complete", codes, raw);

        using var doc = JsonDocument.Parse(message.GetProperty("payload").GetRawText());
        codes.AddRange(MachineDocumentOps.Codes(doc));
        var hasSnapshot =
            doc.RootElement.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.EnumerateObject().SingleOrDefault().Value
                is { ValueKind: JsonValueKind.Object } changed
            && changed.TryGetProperty("state", out var state)
            && state.ValueKind == JsonValueKind.String;
        return (!hasSnapshot || codes.Count > 0, codes, raw);
    }
}

/// <summary>One push event.</summary>
public sealed record MachineChange(JsonElement Raw)
{
    /// <summary>The revision of the snapshot, or 0 when the event carries none.</summary>
    public long Revision =>
        Raw.TryGetProperty("revision", out var revision)
            ? revision.ValueKind switch
            {
                JsonValueKind.Number => revision.GetInt64(),
                JsonValueKind.String => long.Parse(revision.GetString()!),
                _ => 0,
            }
            : 0;

    /// <summary>The state, as the schema spells it.</summary>
    public string? State =>
        Raw.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String
            ? state.GetString()
            : null;

    /// <summary>Whether the instance is gone, which ends the stream.</summary>
    public bool Gone =>
        Raw.TryGetProperty("gone", out var gone) && gone.ValueKind == JsonValueKind.True;

    /// <summary>Whether the client must read the instance again.</summary>
    public bool Reload =>
        Raw.TryGetProperty("reload", out var reload) && reload.ValueKind == JsonValueKind.True;

    /// <summary>The problem's code, or null.</summary>
    public string? ProblemCode =>
        Raw.TryGetProperty("problem", out var problem) && problem.ValueKind == JsonValueKind.Object
            ? problem.GetProperty("code").GetString()
            : null;

    /// <summary>Whether the state is <paramref name="state"/>, however the schema spells an enum value.</summary>
    public bool StateIs(string state) => MachineResult.SameName(State, state);
}
