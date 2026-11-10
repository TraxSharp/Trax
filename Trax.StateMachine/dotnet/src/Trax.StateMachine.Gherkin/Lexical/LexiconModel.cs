namespace Trax.StateMachine.Gherkin.Lexical;

// What a lexicon says, line by line, before anything is inferred. Every node keeps the line it came from, so the
// elaborator can name it in a problem and map each key it writes back to it.

internal abstract record LxValue(int Line);

internal sealed record LxText(string Text, int Line) : LxValue(Line);

internal sealed record LxNumber(double Number, int Line) : LxValue(Line);

internal sealed record LxBool(bool Value, int Line) : LxValue(Line);

internal sealed record LxNull(int Line) : LxValue(Line);

internal sealed record LxList(IReadOnlyList<LxValue> Items, int Line) : LxValue(Line);

/// <summary>A bare name: a constant, or a field of whatever the line reads from.</summary>
internal sealed record LxRef(string Name, int Line) : LxValue(Line);

/// <summary>A field and a value: <c>loaves is 0</c>, in <c>Starts with:</c>, <c>Example:</c> and a reduction's example.</summary>
internal sealed record LxAssign(string Field, LxValue Value, int Line);

/// <summary>
/// A field's type as written: a JSON type or a format (<c>a string</c>, <c>a uuid</c>), a named <c>Type:</c>
/// (<c>a Square</c>), <c>a list of &lt;type&gt;</c>, <c>a map of &lt;type&gt; to &lt;type&gt;</c>, or a bare
/// <c>a list</c> or <c>an object</c>.
/// </summary>
internal sealed record LxTypeRef(
    string Kind,
    string Name,
    LxTypeRef? Element = null,
    LxTypeRef? Key = null
)
{
    internal const string Simple = "simple";
    internal const string Named = "named";
    internal const string List = "list";
    internal const string Map = "map";

    public override string ToString() =>
        Kind switch
        {
            List when Element is not null => $"a list of {Element.Name}",
            Map => $"a map of {Key!.Name} to {Element!.Name}",
            _ => Name,
        };
}

/// <summary>
/// A field: <c>name is a type</c> with its clauses, or, for a context field something writes, only
/// <c>name may be empty</c> (<see cref="Type"/> is null).
/// </summary>
internal sealed record LxField(
    string Name,
    LxTypeRef? Type,
    bool MayBeEmpty,
    double? Minimum,
    double? Maximum,
    IReadOnlyList<string>? Enum,
    LxRule? Where,
    int Line
);

/// <summary>One comparison of a rule, as written: <c>loaves is at least 1</c>, <c>input item is not empty</c>.</summary>
internal sealed record LxAtom(
    string Kind,
    string? Source,
    string Field,
    int Line,
    string? Op = null,
    LxValue? Value = null,
    IReadOnlyList<string>? Values = null,
    string? Type = null,
    string? Name = null
);

/// <summary>A rule: one atom, or several joined by one connective (<c>and</c> or <c>or</c>, never both).</summary>
internal sealed record LxRule(IReadOnlyList<LxAtom> Atoms, string? Connective, int Line);

/// <summary>A context write, <c>target to square</c>: from a field of the line's source, a constant or a value.</summary>
internal sealed record LxSet(string Field, LxValue Value, int Line);

internal sealed record LxReduce(string Kind, IReadOnlyList<LxSet>? Steps, string? Name, int Line);

internal sealed record LxEffect(string Type, string? KeyPrefix, string Example, int Line);

internal sealed record LxOutcome(string Phrase, string Kind, LxRule? When, int Line);

internal sealed record LxLink(string Phrase, string Machine, string Field, int Line);

internal sealed record LxHandler(
    string Name,
    string Type,
    bool? RuleExample,
    IReadOnlyList<LxAssign>? ReductionExample,
    int Line
);

internal sealed record LxConstant(string Name, LxValue Value, int Line);

internal sealed record LxMigration(int From, string Type, int Line);

/// <summary>A train input from a context field, or from the owner (<see cref="From"/> is <c>$owner</c>).</summary>
internal sealed record LxInput(string Field, string From, int Line);

/// <summary>An output field a <c>Stores:</c> line keeps, under its own name or another.</summary>
internal sealed record LxStore(string Output, string Context, int Line);

internal sealed class LxActor(string name, string noun, int line)
{
    internal string Name { get; } = name;
    internal string Noun { get; } = noun;
    internal int Line { get; } = line;
    internal List<string>? Roles { get; set; }
    internal int RolesLine { get; set; }
    internal int AnyLine { get; set; }
}

internal sealed class LxType(string name, int line)
{
    internal string Name { get; } = name;
    internal int Line { get; } = line;
    internal List<string>? Values { get; set; }
    internal int ValuesLine { get; set; }
    internal List<LxField> Has { get; } = [];
}

internal sealed class LxState(string name, string phrase, bool committed, int line)
{
    internal string Name { get; } = name;
    internal string Phrase { get; } = phrase;
    internal bool Committed { get; } = committed;
    internal int Line { get; } = line;
    internal LxRule? Requires { get; set; }
    internal List<LxField>? Context { get; set; }
}

internal sealed class LxTrigger(string name, string phrase, int line)
{
    internal string Name { get; } = name;
    internal string Phrase { get; } = phrase;
    internal int Line { get; } = line;
    internal List<LxField> Input { get; } = [];
    internal LxReduce? Reduce { get; set; }
    internal string? Guard { get; set; }
    internal int GuardLine { get; set; }
    internal LxEffect? Effect { get; set; }
}

internal sealed class LxGuard(string name, LxRule rule, int line)
{
    internal string Name { get; } = name;
    internal LxRule Rule { get; } = rule;
    internal int Line { get; } = line;
    internal string? Holds { get; set; }
    internal int HoldsLine { get; set; }
    internal string? Fails { get; set; }
    internal int FailsLine { get; set; }
}

internal sealed class LxRuns(string train, string state, int line)
{
    internal string Train { get; } = train;
    internal string State { get; } = state;
    internal int Line { get; } = line;

    /// <summary>The inputs in the order written, from <c>Takes:</c> and <c>Input:</c> lines alike.</summary>
    internal List<LxInput> Inputs { get; } = [];

    /// <summary>Whether a <c>Takes:</c> line restates the train's inputs.</summary>
    internal bool Takes { get; set; }
    internal List<LxField>? Returns { get; set; }
    internal int ReturnsLine { get; set; }
    internal List<LxStore> Stores { get; } = [];
    internal List<LxSet> Sets { get; } = [];
    internal List<LxAssign> Example { get; } = [];
    internal List<LxOutcome> Outcomes { get; } = [];
}

internal sealed class LxMachine(string id, string? noun, int line)
{
    internal string Id { get; } = id;
    internal string? Noun { get; } = noun;
    internal int Line { get; } = line;
    internal List<string>? OwnedBy { get; set; }
    internal int OwnedLine { get; set; }

    /// <summary>The line of <c>System</c>, <c>System at:</c> or <c>System keyed by:</c>, or 0.</summary>
    internal int SystemLine { get; set; }
    internal string? SystemAt { get; set; }
    internal List<string>? KeyedBy { get; set; }
    internal string? Start { get; set; }
    internal int StartLine { get; set; }
    internal string? Initial { get; set; }
    internal int InitialLine { get; set; }
    internal int? Version { get; set; }
    internal int VersionLine { get; set; }
    internal List<LxMigration> Migrations { get; } = [];
    internal int DifferentialLine { get; set; }
    internal List<LxAssign>? StartsWith { get; set; }
    internal int StartsWithLine { get; set; }
    internal List<LxField> Context { get; } = [];
    internal List<LxState> States { get; } = [];
    internal List<LxTrigger> Triggers { get; } = [];
    internal List<LxGuard> Guards { get; } = [];
    internal List<LxRuns> Runs { get; } = [];
    internal List<LxLink> Links { get; } = [];
    internal List<LxHandler> Rules { get; } = [];
    internal List<LxHandler> Reductions { get; } = [];
}

internal sealed record LxLabel(string State, string Label, int Line);

internal sealed class LxViewField(string name, string machine, string source, int line)
{
    internal string Name { get; } = name;
    internal string Machine { get; } = machine;
    internal string Source { get; } = source;
    internal int Line { get; } = line;
    internal List<LxLabel> Labels { get; } = [];
}

internal sealed class LxView(string name, int line)
{
    internal string Name { get; } = name;
    internal int Line { get; } = line;
    internal List<string>? Reads { get; set; }
    internal int ReadsLine { get; set; }
    internal List<LxViewField> Fields { get; } = [];
}

internal sealed class LxDoc
{
    internal int? Version { get; set; }
    internal int VersionLine { get; set; }
    internal string? TrainsIn { get; set; }
    internal int TrainsInLine { get; set; }
    internal List<LxActor> Actors { get; } = [];
    internal List<LxConstant> Constants { get; } = [];
    internal List<LxType> Types { get; } = [];
    internal List<LxMachine> Machines { get; } = [];
    internal List<LxView> Views { get; } = [];
}
