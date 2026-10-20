using System.Text.Json.Nodes;
using Trax.Effect.StateMachine.Tests.Fakes;

namespace Trax.Effect.StateMachine.Tests.Helpers;

/// <summary>
/// The C# engine as a conformance testee (<c>Trax.Api.StateMachine/conformance/PROTOCOL.md</c>). It cannot build a
/// machine from an IR, so it serves the test machines whose IRs the kit holds, by name, and a <c>load</c> checks that
/// the IR it is given is the one its machine exports, apart from the test-only <c>differential</c> block: a committed IR
/// that drifted from its C# source fails there.
/// </summary>
public sealed class ConformanceTestee
{
    private static readonly IReadOnlyDictionary<string, Served> Known = new Dictionary<
        string,
        Served
    >
    {
        ["checkout"] = Served.Of(DeclarativeCheckout.Built),
        ["gauge"] = Served.Of(GaugeMachine.Built),
        ["ingest"] = Served.Of(IngestMachine.Built),
        ["reminder"] = Served.Of(ReminderMachine.Built),
        ["turnstile"] = Served.Of(DeclarativeTurnstile.Built),
        ["strict"] = Served.Of(StrictInputMachine.Built),
    };

    private readonly Dictionary<string, Served> _loaded = new();

    /// <summary>Answers one request line with one answer line.</summary>
    public string Answer(string line)
    {
        var request = (JsonObject)JsonNode.Parse(line)!;
        var answer = Handle(request);
        answer["id"] = request["id"]?.DeepClone();
        return answer.ToJsonString();
    }

    private JsonObject Handle(JsonObject request)
    {
        var op = request["op"]!.GetValue<string>();
        if (op == "end")
            return new JsonObject();
        if (op == "canonical")
            return new JsonObject { ["wire"] = Canonical(request) };
        if (op == "load")
            return Load(request);

        var name = request["machine"]!.GetValue<string>();
        if (!_loaded.TryGetValue(name, out var machine))
            return new JsonObject { ["error"] = $"no machine {name} is loaded" };

        return op switch
        {
            "advance" => machine.Step(op, SnapshotOf(request), Trigger(request), request["input"]),
            "outcome" => machine.Step(op, SnapshotOf(request), Trigger(request), request["input"]),
            "timer" => machine.Step(op, SnapshotOf(request), Trigger(request), null),
            "rehydrate" => machine.Rehydrate(request["text"]!.GetValue<string>()),
            "serialize" => new JsonObject { ["wire"] = machine.Serialize(SnapshotOf(request)) },
            _ => new JsonObject { ["unsupported"] = true },
        };
    }

    private JsonObject Load(JsonObject request)
    {
        var name = request["machine"]!.GetValue<string>();
        if (!Known.TryGetValue(name, out var machine))
            return new JsonObject { ["error"] = $"this engine has no machine {name}" };
        if (!JsonNode.DeepEquals(Behaviour(request["ir"]), Behaviour(JsonNode.Parse(machine.Ir))))
            return new JsonObject
            {
                ["error"] =
                    $"the {name} IR differs from what this engine's {name} exports: {machine.Ir}",
            };
        _loaded[name] = machine;
        return new JsonObject { ["loaded"] = true };
    }

    // The IR without its differential block, which is test-only fuzzing input, stripped from a runtime machine: two
    // IRs that differ only there describe the same machine.
    private static JsonNode? Behaviour(JsonNode? ir)
    {
        var copy = ir?.DeepClone() as JsonObject;
        copy?.Remove("differential");
        return copy;
    }

    private static string Trigger(JsonObject request) => request["trigger"]!.GetValue<string>();

    private static Snapshot SnapshotOf(JsonObject request)
    {
        var snapshot = (JsonObject)request["snapshot"]!;
        return new Snapshot
        {
            Machine = snapshot["machine"]!.GetValue<string>(),
            Version = snapshot["version"]!.GetValue<int>(),
            State = snapshot["state"]!.GetValue<string>(),
            Context = (JsonObject)snapshot["context"]!.DeepClone(),
        };
    }

    // A lone surrogate has no UTF-8 encoding and System.Text.Json refuses to read one, so such a string comes as
    // UTF-16 code units.
    private static string Canonical(JsonObject request)
    {
        var context =
            request["context"] is { } text ? (JsonObject)JsonNode.Parse(text.GetValue<string>())!
            : request["stringUnits"] is JsonArray stringUnits
                ? new JsonObject { ["s"] = FromUnits(stringUnits) }
            : new JsonObject { [FromUnits((JsonArray)request["keyUnits"]!)] = 1 };
        return TestTurnstile.Machine.Serialize(
            new Snapshot
            {
                Machine = "m",
                Version = 1,
                State = "S",
                Context = context,
            }
        );
    }

    private static string FromUnits(JsonArray units) =>
        new(units.Select(u => (char)u!.GetValue<int>()).ToArray());

    /// <summary>One served machine, its type arguments closed over.</summary>
    private sealed record Served(
        string Ir,
        Func<string, Snapshot, string, JsonNode?, AdvanceResult> Apply,
        Func<string, RehydrationResult> Read,
        Func<Snapshot, string> Write
    )
    {
        public static Served Of<TState, TTrigger>(BuiltMachine<TState, TTrigger> built)
            where TState : struct, Enum
            where TTrigger : struct, Enum
        {
            var engine = built.Engine;
            return new Served(
                IrExporter.Export(built),
                (op, snapshot, trigger, input) =>
                    op switch
                    {
                        "outcome" => engine.AdvanceOutcome(snapshot, trigger, input?.DeepClone()),
                        "timer" => engine.AdvanceTimer(snapshot, trigger),
                        _ => engine.Advance(snapshot, trigger, input?.DeepClone()),
                    },
                engine.Rehydrate,
                engine.Serialize
            );
        }

        public JsonObject Step(string op, Snapshot snapshot, string trigger, JsonNode? input) =>
            Apply(op, snapshot, trigger, input) switch
            {
                AdvanceResult.Transitioned t => new JsonObject
                {
                    ["outcome"] = "transitioned",
                    ["wire"] = Write(t.Snapshot),
                },
                AdvanceResult.Rejected r => new JsonObject
                {
                    ["outcome"] = "rejected",
                    ["reason"] = r.Reason.ToWire(),
                },
                var other => new JsonObject { ["error"] = $"unexpected result {other}" },
            };

        public JsonObject Rehydrate(string text) =>
            Read(text) switch
            {
                RehydrationResult.Ok ok => new JsonObject
                {
                    ["result"] = "ok",
                    ["wire"] = Write(ok.Snapshot),
                },
                RehydrationResult.Error e => new JsonObject
                {
                    ["result"] = "error",
                    ["code"] = e.Code.ToWire(),
                },
                var other => new JsonObject { ["error"] = $"unexpected result {other}" },
            };

        public string Serialize(Snapshot snapshot) => Write(snapshot);
    }
}
