using System.Text.Json.Nodes;

namespace Trax.StateMachine.Features;

/// <summary>One machine a view reads: the caller's own instances of a user machine, or a system instance at its key.</summary>
internal sealed record ViewRead(string Machine, string? Key);

/// <summary>
/// One field a view shows: a machine's state (<see cref="Source"/> is <c>state</c>), labelled per state if it gives
/// labels, or one of the machine's context fields.
/// </summary>
internal sealed record ViewField(
    string Name,
    string Machine,
    string Source,
    IReadOnlyDictionary<string, string>? Labels
)
{
    internal const string State = "state";

    internal bool IsState => Source == State;
}

/// <summary>
/// The view shape the glossary and the host file share: what a view reads and the fields it shows. The checks that
/// need the machines (that a read machine exists and is read with its key exactly when it is a system machine) are
/// <see cref="Check"/>'s, given each machine's system key or null.
/// </summary>
internal static class Views
{
    internal static (List<ViewRead> Reads, List<ViewField> Fields) Read(At at, JsonObject view)
    {
        var readsAt = at["reads"];
        var reads = readsAt.Array(at.Required(view, "reads"), minItems: 1);
        var list = new List<ViewRead>(reads.Count);
        for (var i = 0; i < reads.Count; i++)
        {
            var itemAt = readsAt[i];
            var item = itemAt.Object(reads[i]);
            itemAt.Closed(item, "machine", "key");
            var machine = Names.MachineIdAt(itemAt["machine"], itemAt.Required(item, "machine"));
            var key = item.ContainsKey("key")
                ? Names.SystemKeyAt(itemAt["key"], item["key"])
                : null;
            list.Add(new ViewRead(machine, key));
        }

        var fieldsAt = at["fields"];
        var fields = fieldsAt.Object(at.Required(view, "fields"));
        if (fields.Count == 0)
            throw fieldsAt.Fail("must hold at least 1.");
        var shown = new List<ViewField>(fields.Count);
        foreach (var (name, node) in fields)
        {
            var fieldAt = fieldsAt[name];
            Names.FieldNameAt(fieldAt, JsonValue.Create(name));
            var field = fieldAt.Object(node);
            fieldAt.Closed(field, "from", "labels");
            var from = fieldAt["from"].Text(fieldAt.Required(field, "from"));
            var match = Names.ViewSource().Match(from);
            if (!match.Success)
                throw fieldAt["from"].Fail("is not 'machine.field' or 'machine.state'.");
            Dictionary<string, string>? labels = null;
            if (field.ContainsKey("labels"))
            {
                var labelsAt = fieldAt["labels"];
                var given = labelsAt.Object(field["labels"]);
                if (given.Count == 0)
                    throw labelsAt.Fail("must hold at least 1.");
                labels = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (state, label) in given)
                {
                    Names.MemberAt(labelsAt[state], JsonValue.Create(state));
                    labels[state] = labelsAt[state].Text(label, 1, 200);
                }
            }
            shown.Add(new ViewField(name, match.Groups[1].Value, match.Groups[2].Value, labels));
        }
        return (list, shown);
    }

    /// <summary>
    /// Refuses a view that reads a machine <paramref name="systemKeys"/> does not hold, reads one twice, reads a system
    /// machine without its key or a user machine with one, or shows a field of a machine it does not read, or labels a
    /// field that is not a state.
    /// </summary>
    internal static void Check(
        At at,
        IReadOnlyList<ViewRead> reads,
        IReadOnlyList<ViewField> fields,
        IReadOnlyDictionary<string, string?> systemKeys
    )
    {
        var read = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < reads.Count; i++)
        {
            var (machine, key) = reads[i];
            var itemAt = at["reads"][i];
            if (!systemKeys.TryGetValue(machine, out var systemKey))
                throw itemAt["machine"].Fail($"names '{machine}', which is not a machine here.");
            if (!read.Add(machine))
                throw itemAt["machine"].Fail($"reads '{machine}' twice.");
            if (systemKey is null && key is not null)
                throw itemAt["key"]
                    .Fail(
                        $"'{machine}' is a user's machine: a view reads the caller's own, never one by key."
                    );
            if (systemKey is not null && key != systemKey)
                throw itemAt.Fail(
                    $"'{machine}' is a system machine: a view reads it at its key, '{systemKey}'."
                );
        }
        foreach (var field in fields)
        {
            var fieldAt = at["fields"][field.Name];
            if (!read.Contains(field.Machine))
                throw fieldAt["from"].Fail($"'{field.Machine}' is not a machine this view reads.");
            if (field.Labels is not null && !field.IsState)
                throw fieldAt["labels"].Fail("labels a state, and this field is not one.");
        }
    }
}
