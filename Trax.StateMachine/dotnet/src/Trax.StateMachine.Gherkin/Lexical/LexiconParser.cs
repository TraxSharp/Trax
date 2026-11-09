namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>
/// Reads a lexicon's lines into <see cref="LxDoc"/>. A keyword belongs under a fixed kind of block and attaches to the
/// nearest one open, closing every block opened after it; anywhere else it is a problem. Each keyword has one form,
/// and a line not in it is a problem naming the form.
/// </summary>
internal sealed class LexiconParser
{
    private readonly List<LexProblem> _problems = [];
    private readonly LxDoc _doc = new();
    private readonly List<(Block Block, object? Node)> _open = [(Block.Root, null)];

    internal static (LxDoc Doc, List<LexProblem> Problems) Parse(IReadOnlyList<LexLine> lines)
    {
        var parser = new LexiconParser();
        foreach (var line in lines)
            parser.Line(line);
        return (parser._doc, parser._problems);
    }

    private void Line(LexLine line)
    {
        var keyword = line.Keyword;
        var at = _open.FindLastIndex(o => keyword.Under.Contains(o.Block));
        if (at < 0)
        {
            var under = string.Join(
                " or ",
                keyword.Under.Select(b => $"a '{LexiconSyntax.Opener(b)}' block")
            );
            if (keyword.Under is [Block.Root])
                under = "the top level, before any block";
            _problems.Add(new LexProblem(line.Number, $"'{keyword.Text}' belongs under {under}."));
            // A block opened in the wrong place still opens, so the lines under it are not each refused again.
            if (keyword.Opens is { } lost)
                _open.Add((lost, null));
            return;
        }
        _open.RemoveRange(at + 1, _open.Count - at - 1);
        if (line.Broken)
        {
            _open.Add((keyword.Opens!.Value, null));
            return;
        }
        var parent = _open[at].Node;
        object? opened = null;
        try
        {
            var cursor = new Cursor(line);
            opened = Read(line, cursor, _open[at].Block, parent);
            cursor.End();
        }
        catch (FormError error)
        {
            _problems.Add(new LexProblem(line.Number, error.Message));
        }
        if (keyword.Opens is { } block)
            _open.Add((block, opened));
    }

    // Reads one line into its parent, and returns the node a block-opening line opens. A parent is null when the line
    // that opened it was a problem: the line is still read, so its own mistakes are reported, and then dropped.
    private object? Read(LexLine line, Cursor c, Block under, object? parent)
    {
        var n = line.Number;
        switch (line.Keyword.Text)
        {
            case "Version:":
            {
                var version = c.Integer();
                if (under == Block.Root)
                {
                    if (_doc.Version is not null)
                        throw Again("Version:", _doc.VersionLine);
                    _doc.Version = version;
                    _doc.VersionLine = n;
                }
                else if (parent is LxMachine m)
                {
                    if (m.Version is not null)
                        throw Again("Version:", m.VersionLine);
                    m.Version = version;
                    m.VersionLine = n;
                }
                return null;
            }
            case "Trains in:":
            {
                var ns = c.Word("a namespace");
                if (_doc.TrainsIn is not null)
                    throw Again("Trains in:", _doc.TrainsInLine);
                _doc.TrainsIn = ns;
                _doc.TrainsInLine = n;
                return null;
            }
            case "Actor:":
            {
                var actor = new LxActor(c.Word("the actor's name"), c.Text("the actor's noun"), n);
                _doc.Actors.Add(actor);
                return actor;
            }
            case "Roles:":
            {
                var roles = c.Words("a role");
                if (parent is LxActor a)
                {
                    if (a.Roles is not null)
                        throw Again("Roles:", a.RolesLine);
                    a.Roles = roles;
                    a.RolesLine = n;
                }
                return null;
            }
            case "Any signed-in":
                if (parent is LxActor any)
                {
                    if (any.AnyLine != 0)
                        throw Again("Any signed-in", any.AnyLine);
                    any.AnyLine = n;
                }
                return null;
            case "Constant:":
            {
                var name = c.Word("the constant's name");
                c.Expect("is");
                _doc.Constants.Add(new LxConstant(name, c.Value(), n));
                return null;
            }
            case "Type:":
            {
                var type = new LxType(c.Word("the type's name"), n);
                _doc.Types.Add(type);
                return type;
            }
            case "Values:":
            {
                var values = TypeValues(c);
                if (parent is LxType t)
                {
                    if (t.Values is not null)
                        throw Again("Values:", t.ValuesLine);
                    t.Values = values;
                    t.ValuesLine = n;
                }
                return null;
            }
            case "Has:":
                (parent as LxType)?.Has.Add(c.Field(partial: false));
                return null;
            case "Machine:":
            {
                var id = c.Word("the machine's id");
                var noun = c.AtEnd ? null : c.Text("the machine's noun");
                var machine = new LxMachine(id, noun, n);
                _doc.Machines.Add(machine);
                return machine;
            }
            case "View:":
            {
                var view = new LxView(c.Text("the view's name"), n);
                _doc.Views.Add(view);
                return view;
            }
            case "Reads:":
            {
                var reads = c.Words("a machine");
                if (parent is LxView v)
                {
                    if (v.Reads is not null)
                        throw Again("Reads:", v.ReadsLine);
                    v.Reads = reads;
                    v.ReadsLine = n;
                }
                return null;
            }
            case "Field:":
            {
                var name = c.Word("the field's name");
                c.Expect("from");
                var possessive = c.Word("<machine>'s");
                if (!possessive.EndsWith("'s", StringComparison.Ordinal) || possessive.Length < 3)
                    throw c.Form($"'{possessive}' is not a machine's: write '<machine>'s <field>'");
                var source = c.Word("the field or state it shows");
                var field = new LxViewField(name, possessive[..^2], source, n);
                (parent as LxView)?.Fields.Add(field);
                return field;
            }
            case "Label:":
            {
                var label = new LxLabel(c.Word("a state"), c.Text("the label"), n);
                (parent as LxViewField)?.Labels.Add(label);
                return null;
            }
        }
        return parent switch
        {
            LxMachine machine => ReadMachine(line, c, machine),
            LxState state => ReadState(line, c, state),
            LxTrigger trigger => ReadTrigger(line, c, trigger),
            LxGuard guard => ReadGuard(line, c, guard),
            LxRuns runs => ReadRuns(line, c, runs),
            _ => Discard(line, c, under),
        };
    }

    // The parent was a problem: read the line for its own form against a throwaway parent, then drop it.
    private object? Discard(LexLine line, Cursor c, Block under) =>
        under switch
        {
            Block.Machine => ReadMachine(line, c, new LxMachine("x", null, 0)),
            Block.State => ReadState(line, c, new LxState("X", "x", false, 0)),
            Block.Trigger => ReadTrigger(line, c, new LxTrigger("X", "x", 0)),
            Block.Guard => ReadGuard(line, c, new LxGuard("x", new LxRule([], null, 0), 0)),
            Block.Runs => ReadRuns(line, c, new LxRuns("X", "X", 0)),
            _ => null,
        };

    private object? ReadMachine(LexLine line, Cursor c, LxMachine m)
    {
        var n = line.Number;
        switch (line.Keyword.Text)
        {
            case "Owned by:":
                if (m.OwnedBy is not null)
                    throw Again("Owned by:", m.OwnedLine);
                m.OwnedBy = c.Words("an actor");
                m.OwnedLine = n;
                return null;
            case "System":
            case "System at:":
            case "System keyed by:":
                if (m.SystemLine != 0)
                    throw Again("System", m.SystemLine);
                if (line.Keyword.Text == "System at:")
                    m.SystemAt = c.Word("the system key");
                if (line.Keyword.Text == "System keyed by:")
                    m.KeyedBy = c.Words("a key part");
                m.SystemLine = n;
                return null;
            case "Started by:":
                if (m.Start is not null)
                    throw Again("Started by:", m.StartLine);
                m.Start = c.Text("the phrase");
                m.StartLine = n;
                return null;
            case "Begins in:":
                if (m.Initial is not null)
                    throw Again("Begins in:", m.InitialLine);
                m.Initial = c.Word("a state");
                m.InitialLine = n;
                return null;
            case "Migrates from:":
            {
                var from = c.Integer();
                c.Expect("with");
                m.Migrations.Add(new LxMigration(from, c.Word("the migration's type"), n));
                return null;
            }
            case "Differential":
                if (m.DifferentialLine != 0)
                    throw Again("Differential", m.DifferentialLine);
                m.DifferentialLine = n;
                return null;
            case "Starts with:":
                if (m.StartsWith is not null)
                    throw Again("Starts with:", m.StartsWithLine);
                m.StartsWith = c.Assignments();
                m.StartsWithLine = n;
                return null;
            case "Context:":
                m.Context.Add(c.Field(partial: true));
                return null;
            case "State:":
            {
                var name = c.Word("the state's name");
                var phrase = c.Text("the state's phrase");
                var committed = false;
                if (!c.AtEnd)
                {
                    c.Expect("committed");
                    committed = true;
                }
                var state = new LxState(name, phrase, committed, n);
                m.States.Add(state);
                return state;
            }
            case "Trigger:":
            {
                var trigger = new LxTrigger(
                    c.Word("the trigger's name"),
                    c.Text("the trigger's phrase"),
                    n
                );
                m.Triggers.Add(trigger);
                return trigger;
            }
            case "Guard:":
            {
                var name = c.Word("the guard's name");
                c.Expect("when");
                var guard = new LxGuard(name, c.Rule(), n);
                m.Guards.Add(guard);
                return guard;
            }
            case "Runs:":
            {
                var train = c.Word("the train");
                c.Expect("in");
                var runs = new LxRuns(train, c.Word("the state it runs in"), n);
                m.Runs.Add(runs);
                return runs;
            }
            case "Link:":
            {
                var phrase = c.Text("the link's phrase");
                c.Expect("to");
                var machine = c.Word("a machine");
                c.Expect("by");
                m.Links.Add(new LxLink(phrase, machine, c.Word("a context field"), n));
                return null;
            }
            case "Rule:":
            {
                var name = c.Word("the rule's name");
                c.Expect("with");
                var type = c.Word("the rule's type");
                c.Comma();
                c.Expect("example");
                var example = c.Next("true or false");
                if (!example.Is("true") && !example.Is("false"))
                    throw c.Form("a rule's example is true or false");
                m.Rules.Add(new LxHandler(name, type, example.Is("true"), null, n));
                return null;
            }
            case "Reduction:":
            {
                var name = c.Word("the reduction's name");
                c.Expect("with");
                var type = c.Word("the reduction's type");
                c.Comma();
                c.Expect("example");
                m.Reductions.Add(new LxHandler(name, type, null, c.Assignments(), n));
                return null;
            }
        }
        throw new InvalidOperationException($"{line.Keyword.Text} has no reader under Machine:.");
    }

    private object? ReadState(LexLine line, Cursor c, LxState state)
    {
        switch (line.Keyword.Text)
        {
            case "Requires:":
                if (state.Requires is not null)
                    throw Again("Requires:", state.Requires.Line);
                state.Requires = c.Rule();
                return null;
            case "Context:":
                (state.Context ??= []).Add(c.Field(partial: false));
                return null;
        }
        throw new InvalidOperationException($"{line.Keyword.Text} has no reader under State:.");
    }

    private object? ReadTrigger(LexLine line, Cursor c, LxTrigger trigger)
    {
        var n = line.Number;
        void Reduce(LxReduce reduce)
        {
            if (trigger.Reduce is { } earlier)
                throw new FormError(
                    $"a trigger reduces once, and line {earlier.Line} already does: one of Sets:, Keeps, Clears, "
                        + "Resets or Reduces with:."
                );
            trigger.Reduce = reduce;
        }
        switch (line.Keyword.Text)
        {
            case "Input:":
                trigger.Input.Add(c.Field(partial: false));
                return null;
            case "Sets:":
                Reduce(new LxReduce("set", c.Sets(), null, n));
                return null;
            case "Keeps":
                Reduce(new LxReduce("keep", null, null, n));
                return null;
            case "Clears":
                Reduce(new LxReduce("clear", null, null, n));
                return null;
            case "Resets":
                Reduce(new LxReduce("reset", null, null, n));
                return null;
            case "Reduces with:":
                Reduce(new LxReduce("custom", null, c.Word("a reduction"), n));
                return null;
            case "Guarded by:":
                if (trigger.Guard is not null)
                    throw Again("Guarded by:", trigger.GuardLine);
                trigger.Guard = c.Word("a guard");
                trigger.GuardLine = n;
                return null;
            case "Effect:":
            {
                if (trigger.Effect is { } earlier)
                    throw Again("Effect:", earlier.Line);
                var type = c.Word("the effect's type");
                string? prefix = null;
                c.Comma();
                if (c.Peek.Is("key"))
                {
                    c.Expect("key");
                    c.Expect("prefix");
                    prefix = c.Text("the key prefix");
                    c.Comma();
                }
                c.Expect("example");
                trigger.Effect = new LxEffect(type, prefix, c.Text("the example receipt"), n);
                return null;
            }
        }
        throw new InvalidOperationException($"{line.Keyword.Text} has no reader under Trigger:.");
    }

    private object? ReadGuard(LexLine line, Cursor c, LxGuard guard)
    {
        var n = line.Number;
        switch (line.Keyword.Text)
        {
            case "Holds:":
                if (guard.Holds is not null)
                    throw Again("Holds:", guard.HoldsLine);
                guard.Holds = c.Text("the phrase");
                guard.HoldsLine = n;
                return null;
            case "Fails:":
                if (guard.Fails is not null)
                    throw Again("Fails:", guard.FailsLine);
                guard.Fails = c.Text("the phrase");
                guard.FailsLine = n;
                return null;
        }
        throw new InvalidOperationException($"{line.Keyword.Text} has no reader under Guard:.");
    }

    private object? ReadRuns(LexLine line, Cursor c, LxRuns runs)
    {
        var n = line.Number;
        switch (line.Keyword.Text)
        {
            case "Input:":
            {
                var field = c.Word("the train's input field");
                c.Expect("from");
                var from = c.Peek.Is("the") ? Owner(c) : c.Word("a context field, or 'the owner'");
                runs.Inputs.Add(new LxInput(field, from, n));
                return null;
            }
            case "Takes:":
                if (runs.Takes)
                    throw new FormError(
                        "the train's inputs are already listed on an earlier 'Takes:' line."
                    );
                runs.Takes = true;
                foreach (var field in c.Words("an input field"))
                    runs.Inputs.Add(new LxInput(field, field, n));
                return null;
            case "Returns:":
                if (runs.Returns is not null)
                    throw Again("Returns:", runs.ReturnsLine);
                runs.Returns = c.Fields();
                runs.ReturnsLine = n;
                return null;
            case "Stores:":
                do
                {
                    var output = c.Word("an output field");
                    var into = output;
                    if (c.Peek.Is("as"))
                    {
                        c.Expect("as");
                        into = c.Word("a context field");
                    }
                    runs.Stores.Add(new LxStore(output, into, n));
                } while (c.TryComma());
                return null;
            case "Sets:":
                runs.Sets.AddRange(c.Sets());
                return null;
            case "Example:":
                runs.Example.AddRange(c.Assignments());
                return null;
            case "Outcome:":
            {
                var phrase = c.Text("the outcome's phrase");
                c.Expect("when");
                if (c.Peek.Is("it"))
                {
                    c.Expect("it");
                    string kind;
                    if (c.Peek.Is("fails"))
                    {
                        c.Expect("fails");
                        kind = "failed";
                    }
                    else
                    {
                        c.Expect("is");
                        if (c.Peek.Is("done"))
                        {
                            c.Expect("done");
                            kind = "done";
                        }
                        else
                        {
                            c.Expect("called");
                            c.Expect("off");
                            kind = "cancelled";
                        }
                    }
                    runs.Outcomes.Add(new LxOutcome(phrase, kind, null, n));
                }
                else
                    runs.Outcomes.Add(new LxOutcome(phrase, "done", c.Rule(), n));
                return null;
            }
        }
        throw new InvalidOperationException($"{line.Keyword.Text} has no reader under Runs:.");
    }

    private static string Owner(Cursor c)
    {
        c.Expect("the");
        c.Expect("owner");
        return "$owner";
    }

    // Values: "S", "M", "L"   or   Values: A to J, each followed by 1 to 10
    private static List<string> TypeValues(Cursor c)
    {
        if (c.Peek.Kind == TokenKind.Text)
        {
            var listed = new List<string> { c.Text("a value") };
            while (c.TryComma())
                listed.Add(c.Text("a value"));
            return listed;
        }
        var values = Range(c);
        if (c.TryComma())
        {
            c.Expect("each");
            c.Expect("followed");
            c.Expect("by");
            var tails = Range(c);
            values = values.SelectMany(v => tails.Select(t => v + t)).ToList();
        }
        return values;
    }

    private static List<string> Range(Cursor c)
    {
        var from = c.Word("the first of a range");
        c.Expect("to");
        var to = c.Word("the last of a range");
        if (int.TryParse(from, out var a) && int.TryParse(to, out var b) && a <= b && b - a < 1000)
            return Enumerable
                .Range(a, b - a + 1)
                .Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .ToList();
        if (
            from.Length == 1
            && to.Length == 1
            && char.IsLetter(from[0])
            && char.IsLetter(to[0])
            && from[0] <= to[0]
            && char.IsUpper(from[0]) == char.IsUpper(to[0])
        )
            return Enumerable
                .Range(from[0], to[0] - from[0] + 1)
                .Select(i => ((char)i).ToString())
                .ToList();
        throw c.Form(
            $"'{from} to {to}' is not a range: two letters or two whole numbers, the first not after the last"
        );
    }

    private static FormError Again(string keyword, int line) =>
        new($"'{keyword}' is already given at line {line}, and is given once.");
}

/// <summary>A line not in its keyword's form: the message names the form.</summary>
internal sealed class FormError(string message) : Exception(message);
