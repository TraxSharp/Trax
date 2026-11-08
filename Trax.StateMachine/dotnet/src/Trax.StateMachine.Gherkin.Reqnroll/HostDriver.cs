using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.StateMachine.Features;
using Trax.StateMachine.Persistence;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.StateMachine.Gherkin.Reqnroll;

/// <summary>
/// A host in the test process, answering a scenario as it answers its clients (ADR <c>Trax.Docs/adr/0058</c>). A
/// person's start, triggers and view reads go over GraphQL with that person's own credential, so a trigger the host does
/// not serve them is not served and a rule that does not admit them is <c>FORBIDDEN</c>, as the host says it. A Given
/// is written through the host's store and sets off nothing; a step no person takes fires as the host's server does.
/// Time passing is waited for exactly, at most <see cref="MaxPass"/> a step, then the host is given
/// <see cref="TraxFeatureDriverOptions.Slack"/> to fire what fell due and run the policies it set off.
/// </summary>
internal sealed class HostDriver(
    TraxFeatureDriverOptions options,
    Glossary glossary,
    string scenarioId
) : IScenarioDriver
{
    private readonly HostFile _host = options.Host.GetRequiredService<HostFile>();
    private readonly Dictionary<InstanceKey, Guid> _ids = [];
    private readonly Dictionary<string, FeatureCredential> _credentials = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, GlossaryActor> _actors = new(StringComparer.Ordinal);

    // The newest history row when the current When began: what any instance entered after it is the When's cascade.
    private long _when;

    private IReadOnlyDictionary<string, IMachineInternals> Machines =>
        options
            .Host.GetServices<IMachine>()
            .OfType<IMachineInternals>()
            .GroupBy(m => ((IMachine)m).Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    public async ValueTask<Answer> Start(Person person, string machine)
    {
        _actors[person.Name] = person.Actor;
        var requestId = RequestId();
        var answer = await Send(
            person.Name,
            new Operation(
                machine,
                "start",
                $"mutation {{ machines {{ {FieldOf(machine)} {{ start(requestId: \"{requestId}\") {{ id state problem {{ code message }} }} }} }} }}",
                new JsonObject { ["requestId"] = requestId }
            ),
            ["machines", FieldOf(machine), "start"]
        );
        // A refused start answers a problem and no id.
        if (answer is { Answer: Answer.Moved, Result: { } result })
            _ids[new InstanceKey(machine, person.Name)] = Guid.Parse(
                result["id"]!.GetValue<string>()
            );
        return answer.Answer;
    }

    public async ValueTask Seed(InstanceKey key, string state, JsonObject? context)
    {
        var id = await IdOf(key);
        await using var scope = options.Host.CreateAsyncScope();
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();
        var row =
            await ServerFire.RowOf(data, key.Machine, id, CancellationToken.None)
            ?? throw new InvalidOperationException($"{Describe(key)} has no row to arrange.");
        var seeded = await ServerFire.Seed(
            scope.ServiceProvider,
            data,
            Machines,
            row,
            state,
            context,
            CancellationToken.None
        );
        var why = seeded switch
        {
            ServerFire.Fired.Failed failed => failed.Why,
            ServerFire.Fired.Busy busy => busy.Why,
            _ => null,
        };
        if (why is not null)
            throw new InvalidOperationException(
                $"{Describe(key)} could not be put in {state}: {why}."
            );
    }

    public async ValueTask BeginWhen()
    {
        await using var scope = options.Host.CreateAsyncScope();
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();
        _when = await data.SnapshotTransitions.MaxAsync(t => (long?)t.Id) ?? 0;
    }

    public async ValueTask<IReadOnlyList<string>> Cascaded(InstanceKey key)
    {
        if (!_ids.ContainsKey(key) && (key.Owner is not null || key.Key is not null))
            return [];
        return EnteredAfter(await History(key, await IdOf(key)), _when);
    }

    public async ValueTask<(string State, JsonObject Context)?> Read(InstanceKey key)
    {
        if (!_ids.ContainsKey(key) && (key.Owner is not null || key.Key is not null))
            return null;
        var id = await IdOf(key);
        await using var scope = options.Host.CreateAsyncScope();
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();
        var row = await data
            .SnapshotDrafts.AsNoTracking()
            .Where(d => d.Machine == key.Machine && d.Id == id)
            .Select(d => new { d.State, d.Context })
            .FirstOrDefaultAsync();
        return row is null ? null : (row.State, JsonNode.Parse(row.Context)?.AsObject() ?? []);
    }

    public async ValueTask<Answer> Fire(
        Person? person,
        InstanceKey key,
        string trigger,
        JsonObject? input
    )
    {
        if (person is null)
            return await FireAsServer(key, trigger, input);
        _actors[person.Name] = person.Actor;
        if (key.Owner is not null && !_ids.ContainsKey(key))
            return new Answer.Refused(RefusalCode.NotFound, "No draft with that id.");
        var id = await IdOf(key);
        var field = FieldOf(key.Machine);
        var operation = char.ToLowerInvariant(trigger[0]) + trigger[1..];
        var inputArgument = input is null ? "" : $"input: {Literal(input)}, ";
        var requestId = RequestId();
        var variables = new JsonObject { ["id"] = id.ToString(), ["requestId"] = requestId };
        if (input is not null)
            variables["input"] = input.DeepClone();
        var answer = await Send(
            person.Name,
            new Operation(
                key.Machine,
                operation,
                $"mutation {{ machines {{ {field} {{ {operation}(id: \"{id}\", {inputArgument}requestId: \"{requestId}\") {{ state problem {{ code message }} }} }} }} }}",
                variables
            ),
            ["machines", field, operation]
        );
        await Settle();
        return answer.Answer;
    }

    public async ValueTask<Answer> Outcome(
        InstanceKey key,
        string state,
        string kind,
        JsonNode? output
    )
    {
        // The host's own train runs and says how it ended: the first move out of the state after the instance last entered
        // it, from its history, so a state it passed through on the way, or the move out of an earlier visit to the same
        // state, is not mistaken for where this outcome took it.
        var id = await IdOf(key);
        var deadline = DateTimeOffset.UtcNow + options.Slack;
        var entered = Entered(await History(key, id), state);
        while (true)
        {
            if (LeftBy(await History(key, id), state, entered) is { } left)
            {
                // The host may have applied the outcome before this When began: its move and what it set off are
                // still this When's cascade.
                _when = Math.Min(_when, left.Id - 1);
                await Settle();
                return new Answer.Moved(left.To);
            }
            if (DateTimeOffset.UtcNow > deadline)
                return new Answer.Refused(
                    RefusalCode.InternalError,
                    $"The train {state} invokes did not end within {options.Slack.TotalSeconds:0.#} seconds."
                );
            await Task.Delay(100);
        }
    }

    /// <summary>
    /// One row of an instance's transition history: its revision and the move it wrote, the row's identity, and whether
    /// it was an arranging (a start or a save) rather than a move.
    /// </summary>
    internal readonly record struct Move(
        long Revision,
        string? From,
        string To,
        long Id = 0,
        bool Arranged = false
    );

    /// <summary>
    /// The states entered by the moves written after row <paramref name="mark"/>, in order: what a When and the policies,
    /// timers and outcomes after it did. A Given's arranging is not a move.
    /// </summary>
    internal static IReadOnlyList<string> EnteredAfter(IReadOnlyList<Move> history, long mark) =>
        history
            .Where(m => m.Id > mark && !m.Arranged)
            .OrderBy(m => m.Revision)
            .Select(m => m.To)
            .ToList();

    /// <summary>The revision at which the instance last entered <paramref name="state"/>, or 0 if it never did.</summary>
    internal static long Entered(IReadOnlyList<Move> history, string state) =>
        history.Where(m => m.To == state).Select(m => m.Revision).DefaultIfEmpty(0).Max();

    /// <summary>
    /// Where the instance went when it left <paramref name="state"/> after <paramref name="entered"/>, or null if it has
    /// not left it since.
    /// </summary>
    internal static string? Left(IReadOnlyList<Move> history, string state, long entered) =>
        LeftBy(history, state, entered)?.To;

    private static Move? LeftBy(IReadOnlyList<Move> history, string state, long entered) =>
        history
            .Where(m => m.Revision > entered && m.From == state && m.To != state)
            .OrderBy(m => m.Revision)
            .Select(m => (Move?)m)
            .FirstOrDefault();

    private async Task<List<Move>> History(InstanceKey key, Guid id)
    {
        await using var scope = options.Host.CreateAsyncScope();
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();
        var row = await ServerFire.RowOf(data, key.Machine, id, CancellationToken.None);
        return await data
            .SnapshotTransitions.AsNoTracking()
            .Where(t => t.DraftRowId == row)
            .Select(t => new Move(
                t.Revision,
                t.FromState,
                t.ToState,
                t.Id,
                t.Kind == SnapshotTransitionKind.Start || t.Kind == SnapshotTransitionKind.Save
            ))
            .ToListAsync();
    }

    /// <summary>
    /// The longest one step's time passing may be against a host: it is waited for in real time, so a scenario that
    /// says more is refused rather than left to block the test run.
    /// </summary>
    internal static readonly TimeSpan MaxPass = TimeSpan.FromMinutes(10);

    public async ValueTask Pass(TimeSpan time)
    {
        if (time > MaxPass)
            throw new InvalidOperationException(
                $"{time} cannot pass against a host: time passing is waited for in real time, and a step may wait at most {MaxPass}. Run this scenario in memory."
            );
        await Task.Delay(time);
        await Settle();
    }

    public async ValueTask<(IReadOnlyList<string>? Values, Answer? Refused)> View(
        Person person,
        string view,
        string field
    )
    {
        _actors[person.Name] = person.Actor;
        if (!_host.Views.TryGetValue(view, out var served))
            return (null, new Answer.NotServed($"no view {view}"));
        var shown = served.Fields.First(f => f.Name == field);
        var viewField = ViewFieldOf(view);
        var machineField = FieldOf(shown.Machine);
        var answer = await Send(
            person.Name,
            new Operation(
                null,
                viewField,
                $"{{ views {{ {viewField} {{ {machineField} {{ {field} }} }} }} }}",
                []
            ),
            ["views", viewField, machineField]
        );
        if (answer.Answer is not Answer.Moved)
            return (null, answer.Answer);
        var node = answer.Node;
        var values = node switch
        {
            JsonArray rows => rows.Select(r => Text(r?[field])).ToList(),
            JsonObject one => [Text(one[field])],
            _ => [],
        };
        return (values, null);
    }

    // Waits for the host to fire what is due and the policies it owes, within the slack.
    private async Task Settle()
    {
        var deadline = DateTimeOffset.UtcNow + options.Slack;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = options.Host.CreateAsyncScope();
            var data = scope.ServiceProvider.GetRequiredService<IDataContext>();
            var now = DateTimeOffset.UtcNow;
            var ids = _ids.Values.ToList();
            var due = await data.SnapshotDrafts.AnyAsync(d =>
                ids.Contains(d.Id) && d.DueAt != null && d.DueAt <= now
            );
            var owed = _host.Policies.Count > 0 && await data.MachinePolicyFires.AnyAsync();
            if (!due && !owed)
                return;
            await Task.Delay(100);
        }
    }

    private async Task<Answer> FireAsServer(InstanceKey key, string trigger, JsonObject? input)
    {
        var id = await IdOf(key);
        await using var scope = options.Host.CreateAsyncScope();
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();
        var row = await Persistence.FromIr.ServerFire.RowOf(
            data,
            key.Machine,
            id,
            CancellationToken.None
        );
        if (row is null)
            return new Answer.Refused(RefusalCode.NotFound, "No draft with that id.");
        var fired = await Persistence.FromIr.ServerFire.Fire(
            scope.ServiceProvider,
            data,
            Machines,
            row.Value,
            trigger,
            input,
            TransitionNote.Command(DraftOwner.System, trigger),
            null,
            CancellationToken.None
        );
        await Settle();
        return fired switch
        {
            Persistence.FromIr.ServerFire.Fired.Moved moved => new Answer.Moved(moved.State),
            Persistence.FromIr.ServerFire.Fired.Refused refused => new Answer.Refused(
                refused.Code,
                refused.Message
            ),
            Persistence.FromIr.ServerFire.Fired.Failed failed =>
                throw new InvalidOperationException(
                    $"{trigger} could not be fired on {Describe(key)}: {failed.Why}."
                ),
            Persistence.FromIr.ServerFire.Fired.Busy busy => throw new InvalidOperationException(
                $"{trigger} could not be fired on {Describe(key)}: {busy.Why}."
            ),
            _ => throw new InvalidOperationException("unknown result"),
        };
    }

    // A user's instance by the id its start answered, started as its owner when a Given names it first; a system
    // instance at its key, which the host started.
    private async Task<Guid> IdOf(InstanceKey key)
    {
        if (_ids.TryGetValue(key, out var id))
            return id;
        if (key.Key is not null)
        {
            // A keyed system machine's instance is started by host code, as the host's own trains start one.
            await using var scope = options.Host.CreateAsyncScope();
            var started = await scope
                .ServiceProvider.GetRequiredService<IMachineInstances>()
                .Start(key.Machine, MachineKey.Of([.. key.Parts]));
            return _ids[key] = started.Id;
        }
        if (key.Owner is null)
        {
            var systemKey =
                _host.Machines[key.Machine].SystemKey
                ?? throw new InvalidOperationException($"{key.Machine} has no system key.");
            return _ids[key] = SystemInstanceStarter.IdOf(key.Machine, systemKey);
        }
        var owner = new Person(key.Owner, Actor(key.Owner, key.Machine));
        if (await Start(owner, key.Machine) is not Answer.Moved)
            throw new InvalidOperationException(
                $"{key.Owner} could not start their {key.Machine} to arrange it."
            );
        return _ids[key];
    }

    private GlossaryActor Actor(string person, string machine) =>
        _actors.TryGetValue(person, out var actor)
            ? actor
            : glossary.Actors[glossary.Machines[machine].OwnedBy![0]];

    /// <summary>A person's credential, as their own steps send it, for a step the sample registered.</summary>
    internal FeatureCredential CredentialOf(string person, GlossaryActor actor)
    {
        _actors.TryAdd(person, actor);
        return CredentialOf(person);
    }

    /// <summary>The id of an instance the scenario started, or null if it started none.</summary>
    internal Guid? InstanceOf(InstanceKey key) => _ids.TryGetValue(key, out var id) ? id : null;

    private FeatureCredential CredentialOf(string person)
    {
        if (_credentials.TryGetValue(person, out var credential))
            return credential;
        var actor = _actors[person];
        return _credentials[person] = options.Credential(
            new FeatureActor(actor.Name, actor.Roles, actor.AnyAuthenticated),
            person,
            scenarioId
        );
    }

    private sealed record Sent(Answer Answer, JsonObject? Result, JsonNode? Node);

    /// <summary>
    /// One request: a machine's operation (<paramref name="Machine"/> set) or a view's read, as a hand-written document
    /// and as the variables its persisted document takes.
    /// </summary>
    private sealed record Operation(
        string? Machine,
        string Name,
        string Document,
        JsonObject Variables
    );

    // The codes a host answers a request with before it reaches a machine or a view: the request is not one it runs,
    // so no step may read them as the machine's answer (a denial, or a trigger not served).
    private static readonly HashSet<string> PostureCodes = new(StringComparer.Ordinal)
    {
        "PERSISTED_OPERATION_REQUIRED",
        "PERSISTED_OPERATION_ID_MISMATCH",
        "CLIENT_OUTDATED",
        "SERVER_OUTDATED",
        "HC0020",
    };

    // The persisted id the manifest lists for an operation, or null when it lists none. A machine whose IR the host
    // serves differently from the one the manifest was exported with is refused: its ids are another version's.
    private string? PersistedId(ClientManifest manifest, Operation operation)
    {
        if (operation.Machine is null)
            return manifest.Views.FirstOrDefault(v => v.Operation == operation.Name)?.Id;
        var machine = manifest.Machines.FirstOrDefault(m => m.Id == operation.Machine);
        if (machine is null)
            return null;
        if (
            Machines.TryGetValue(operation.Machine, out var served)
            && ((IMachine)served).SchemaHash is { } hash
            && hash != machine.IrHash
        )
            throw new InvalidOperationException(
                $"The client manifest was exported from another version of {operation.Machine} than the host serves: export it again."
            );
        return machine.Documents.FirstOrDefault(d => d.Operation == operation.Name)?.Id;
    }

    // Sends the operation as the person, by its persisted id when a manifest is set and as its document when not; the
    // answer at path. A field the schema does not have for them is not served.
    private async Task<Sent> Send(string person, Operation operation, string[] path)
    {
        JsonObject sent;
        if (options.Manifest is { } manifest)
        {
            if (PersistedId(manifest, operation) is not { } id)
                return new Sent(
                    new Answer.NotServed(
                        $"the client manifest has no document for {operation.Machine ?? "the view"} {operation.Name}"
                    ),
                    null,
                    null
                );
            sent = new JsonObject { ["id"] = id, ["variables"] = operation.Variables };
        }
        else
            sent = new JsonObject { ["query"] = operation.Document };
        using var request = new HttpRequestMessage(HttpMethod.Post, options.GraphQLPath)
        {
            Content = new StringContent(sent.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        foreach (var (name, value) in CredentialOf(person).Headers)
            request.Headers.TryAddWithoutValidation(name, value);
        using var response = await options.Client.SendAsync(request);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())?.AsObject() ?? [];
        if (body["errors"] is JsonArray { Count: > 0 } errors)
        {
            var error = errors[0]!;
            var code = error["extensions"]?["code"]?.GetValue<string>();
            if (code is not null && PostureCodes.Contains(code))
                throw new InvalidOperationException(
                    $"The host did not run {operation.Machine ?? "the view"} {operation.Name}: {code}, {error["message"]?.GetValue<string>()}"
                        + (
                            options.Manifest is null
                                ? " Set TraxFeatureDriverOptions.Manifest to the host's client manifest, so each operation is sent by its persisted id."
                                : ""
                        )
                );
            if (body["data"] is null && code is not ("FORBIDDEN" or "UNAUTHENTICATED"))
                return new Sent(
                    new Answer.NotServed(error["message"]?.GetValue<string>() ?? "refused"),
                    null,
                    null
                );
            return new Sent(
                new Answer.Refused(
                    RefusalCodes.TryParse(code, out var parsed) ? parsed : RefusalCode.Forbidden,
                    error["message"]?.GetValue<string>() ?? ""
                ),
                null,
                null
            );
        }
        JsonNode? node = body["data"];
        foreach (var segment in path)
            node = node?[segment];
        if (node is JsonObject result && result["problem"] is JsonObject problem)
            return new Sent(
                new Answer.Refused(
                    RefusalCodes.TryParse(problem["code"]?.GetValue<string>(), out var parsed)
                        ? parsed
                        : RefusalCode.InternalError,
                    problem["message"]?.GetValue<string>() ?? ""
                ),
                result,
                node
            );
        var state = (node as JsonObject)?["state"]?.GetValue<string>() ?? "";
        return new Sent(new Answer.Moved(state), node as JsonObject, node);
    }

    private static string RequestId() => $"f-{Guid.NewGuid():N}";

    private static string Describe(InstanceKey key) =>
        key.Owner is not null ? $"{key.Owner}'s {key.Machine}"
        : key.Key is not null ? $"{key.Machine} {key.Said}"
        : key.Machine;

    // "v-order" is vOrder, as the machines root names it.
    private static string FieldOf(string machine)
    {
        var words = machine.Split('-');
        return words[0]
            + string.Concat(words.Skip(1).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    // "shop sign" is shopSign, as the views root names it.
    private static string ViewFieldOf(string view)
    {
        var pascal = string.Concat(
            view.Split(' ', '-', '_')
                .Where(w => w.Length > 0)
                .Select(w => char.ToUpperInvariant(w[0]) + w[1..])
        );
        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    private static string Text(JsonNode? value) =>
        value switch
        {
            null => "",
            JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
            _ => value.ToJsonString(),
        };

    // A JSON value as a GraphQL literal: an object's keys bare, every value as JSON writes it.
    private static string Literal(JsonNode? node) =>
        node switch
        {
            null => "null",
            JsonObject o => "{ "
                + string.Join(", ", o.Select(kv => $"{kv.Key}: {Literal(kv.Value)}"))
                + " }",
            JsonArray a => "[" + string.Join(", ", a.Select(Literal)) + "]",
            _ => node.ToJsonString(),
        };
}
