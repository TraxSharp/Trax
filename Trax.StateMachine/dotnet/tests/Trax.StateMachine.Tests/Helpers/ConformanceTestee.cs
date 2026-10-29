using System.Text.Json.Nodes;
using Trax.StateMachine.Tests.Fakes;

namespace Trax.StateMachine.Tests.Helpers;

/// <summary>
/// The C# engine as a conformance testee (<c>Trax.StateMachine/conformance/PROTOCOL.md</c>), in one of two ways. By
/// default it serves the compiled test machines whose IRs the kit holds, by name, and a <c>load</c> checks that the IR it
/// is given is the one its machine exports, apart from the test-only <c>differential</c> block: a committed IR that
/// drifted from its C# source fails there. With <c>fromIr</c> it builds each machine from the IR it is loaded with, as
/// a client that holds only the host's manifest does (<see cref="IrMachine"/>).
/// </summary>
public sealed class ConformanceTestee(bool fromIr = false)
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
        ["vault"] = Served.Of(VaultMachine.Built),
        ["wait"] = Served.Of(WaitMachine.Built),
    };

    // The machines the kit serves without the handlers of their custom rules (PROTOCOL.md): a compiled machine has
    // every handler bound, so this testee checks the IR against the compiled machine and then serves it from the IR.
    private static readonly HashSet<string> ServedWithoutHandlers = ["vault"];

    // The machines the kit holds only as an IR, with no C# source to check it against: the planner's wizard, whose IR
    // was written for its cases. Every testee builds them from the IR.
    private static readonly HashSet<string> OnlyAnIr = ["wizard"];

    // The one handler every testee binds: a custom guard named `throws`, which throws.
    private static readonly Dictionary<string, Func<JsonObject, JsonNode?, bool>> KitGuards = new()
    {
        ["throws"] = (_, _) => throw new InvalidOperationException("throws"),
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
            "rehydrate" => machine.Rehydrate(
                request["text"]!.GetValue<string>(),
                request["fromServer"]?.GetValue<bool>() == true
            ),
            "serialize" => new JsonObject { ["wire"] = machine.Serialize(SnapshotOf(request)) },
            "plan" => Planned(machine.Plan(SnapshotOf(request), PlanOf(request))),
            _ => new JsonObject { ["unsupported"] = true },
        };
    }

    private JsonObject Load(JsonObject request)
    {
        var name = request["machine"]!.GetValue<string>();
        if (fromIr || OnlyAnIr.Contains(name))
        {
            try
            {
                _loaded[name] = FromIr(request);
            }
            catch (ArgumentException ex)
            {
                return new JsonObject { ["error"] = ex.Message };
            }
            return new JsonObject { ["loaded"] = true };
        }
        if (!Known.TryGetValue(name, out var machine))
            return new JsonObject { ["error"] = $"this engine has no machine {name}" };
        if (!JsonNode.DeepEquals(Behaviour(request["ir"]), Behaviour(JsonNode.Parse(machine.Ir))))
            return new JsonObject
            {
                ["error"] =
                    $"the {name} IR differs from what this engine's {name} exports: {machine.Ir}",
            };
        _loaded[name] = ServedWithoutHandlers.Contains(name) ? FromIr(request) : machine;
        return new JsonObject { ["loaded"] = true };
    }

    private static Served FromIr(JsonObject request) =>
        Served.Of(new IrMachine((JsonObject)request["ir"]!.DeepClone(), KitGuards));

    // The IR without its differential block, which is test-only fuzzing input, stripped from a runtime machine: two
    // IRs that differ only there describe the same machine.
    private static JsonNode? Behaviour(JsonNode? ir)
    {
        var copy = ir?.DeepClone() as JsonObject;
        copy?.Remove("differential");
        return copy;
    }

    private static (
        string Target,
        PlannedEvent? Fields,
        IReadOnlyList<string> Navigation,
        int? MaxDepth
    ) PlanOf(JsonObject request) =>
        (
            request["target"]!.GetValue<string>(),
            request["fields"] is JsonObject fields
                ? new PlannedEvent(
                    fields["trigger"]!.GetValue<string>(),
                    fields["input"]?.DeepClone()
                )
                : null,
            request["navigation"]!.AsArray().Select(t => t!.GetValue<string>()).ToList(),
            request["maxDepth"]?.GetValue<int>()
        );

    private static JsonObject Planned(PlanResult result) =>
        result switch
        {
            PlanResult.Planned p => new JsonObject
            {
                ["result"] = "plan",
                ["events"] = Events(p.Events),
            },
            PlanResult.Unreachable => new JsonObject { ["result"] = "unreachable" },
            PlanResult.Exceeded => new JsonObject { ["result"] = "exceeded" },
            PlanResult.Undetermined u => new JsonObject
            {
                ["result"] = "undetermined",
                ["trigger"] = u.Trigger,
                ["events"] = Events(u.Events),
            },
            PlanResult.Invalid { Target: { } target } => new JsonObject
            {
                ["result"] = "invalid",
                ["target"] = target,
            },
            PlanResult.Invalid i => new JsonObject
            {
                ["result"] = "invalid",
                ["trigger"] = i.Trigger,
            },
            var other => new JsonObject { ["error"] = $"unexpected result {other}" },
        };

    private static JsonArray Events(IReadOnlyList<PlannedEvent> events)
    {
        var array = new JsonArray();
        foreach (var e in events)
        {
            var written = new JsonObject { ["trigger"] = e.Trigger };
            if (e.Input is not null)
                written["input"] = e.Input.DeepClone();
            array.Add(written);
        }
        return array;
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
        Func<string, bool, RehydrationResult> Read,
        Func<Snapshot, string> Write,
        Func<
            Snapshot,
            (string Target, PlannedEvent? Fields, IReadOnlyList<string> Navigation, int? MaxDepth),
            PlanResult
        > Planner
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
                // Every custom rule of a compiled machine has its handler, so a snapshot from the server reads the same.
                (text, _) => engine.Rehydrate(text),
                engine.Serialize,
                (snapshot, p) => built.Plan(snapshot, p.Target, p.Fields, p.Navigation, p.MaxDepth)
            );
        }

        public static Served Of(IrMachine machine) =>
            new(
                "",
                (op, snapshot, trigger, input) =>
                    op switch
                    {
                        "outcome" => machine.ApplyOutcome(snapshot, trigger, input?.DeepClone()),
                        "timer" => machine.ApplyTimer(snapshot, trigger),
                        _ => machine.Advance(snapshot, trigger, input?.DeepClone()),
                    },
                machine.Rehydrate,
                machine.Serialize,
                (snapshot, p) =>
                    machine.Plan(snapshot, p.Target, p.Fields, p.Navigation, p.MaxDepth)
            );

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

        public JsonObject Rehydrate(string text, bool fromServer) =>
            Read(text, fromServer) switch
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

        public PlanResult Plan(
            Snapshot snapshot,
            (
                string Target,
                PlannedEvent? Fields,
                IReadOnlyList<string> Navigation,
                int? MaxDepth
            ) request
        ) => Planner(snapshot, request);
    }
}
