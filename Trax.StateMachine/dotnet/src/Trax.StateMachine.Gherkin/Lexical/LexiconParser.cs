using System.Globalization;
using Trax.Core.Extensions;

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
    private bool _started;

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
        var first = !_started;
        _started = true;

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
        if (
            string.Equals(keyword.Text, "Version:", StringComparison.Ordinal)
            && _open[at].Block == Block.Root
            && !first
        )
        {
            _problems.Add(
                new LexProblem(line.Number, "'Version: 1' is the first line of a lexicon.")
            );
            return;
        }
        if (line.Broken)
        {
            _open.Add((keyword.Opens.NotNull().Value, null));
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
        catch (FormErrorException error)
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
                return ReadVersion(c, under, parent, n);
            case "Trains in:":
                return ReadTrainsIn(c, n);
            case "Actor:":
            {
                var actor = new LxActor(c.Word("the actor's name"), c.Text("the actor's noun"), n);
                _doc.Actors.Add(actor);
                return actor;
            }
            case "Roles:":
                return ReadRoles(c, parent, n);
            case "Any signed-in":
                return ReadAnySignedIn(parent, n);
            case "Constant:":
                return ReadConstant(c, n);
            case "Type:":
            {
                var type = new LxType(c.Word("the type's name"), n);
                _doc.Types.Add(type);
                return type;
            }
            case "Values:":
                return ReadValues(c, parent, n);
            case "Has:":
                (parent as LxType)?.Has.Add(c.Field(partial: false));
                return null;
            case "Machine:":
                return OpenMachine(c, n);
            case "View:":
            {
                var view = new LxView(c.Text("the view's name"), n);
                _doc.Views.Add(view);
                return view;
            }
            case "Reads:":
                return ReadReads(c, parent, n);
            case "Field:":
                return ReadViewField(c, parent, n);
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

    private object? ReadVersion(Cursor c, Block under, object? parent, int n)
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

    private object? ReadTrainsIn(Cursor c, int n)
    {
        var ns = c.Word("a namespace");
        if (_doc.TrainsIn is not null)
            throw Again("Trains in:", _doc.TrainsInLine);
        _doc.TrainsIn = ns;
        _doc.TrainsInLine = n;
        return null;
    }

    private static object? ReadRoles(Cursor c, object? parent, int n)
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

    private static object? ReadAnySignedIn(object? parent, int n)
    {
        if (parent is LxActor any)
        {
            if (any.AnyLine != 0)
                throw Again("Any signed-in", any.AnyLine);
            any.AnyLine = n;
        }
        return null;
    }

    private object? ReadConstant(Cursor c, int n)
    {
        var name = c.Word("the constant's name");
        c.Expect("is");
        _doc.Constants.Add(new LxConstant(name, c.Value(), n));
        return null;
    }

    private static object? ReadValues(Cursor c, object? parent, int n)
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

    private LxMachine OpenMachine(Cursor c, int n)
    {
        var id = c.Word("the machine's id");
        var noun = c.AtEnd ? null : c.Text("the machine's noun");
        var machine = new LxMachine(id, noun, n);
        _doc.Machines.Add(machine);
        return machine;
    }

    private static object? ReadReads(Cursor c, object? parent, int n)
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

    private static LxViewField ReadViewField(Cursor c, object? parent, int n)
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

    // The parent was a problem: read the line for its own form against a throwaway parent, then drop it.
    private static object? Discard(LexLine line, Cursor c, Block under) =>
        under switch
        {
            Block.Machine => ReadMachine(line, c, new LxMachine("x", noun: null, 0)),
            Block.State => ReadState(line, c, new LxState("X", "x", committed: false, 0)),
            Block.Trigger => ReadTrigger(line, c, new LxTrigger("X", "x", 0)),
            Block.Guard => ReadGuard(
                line,
                c,
                new LxGuard("x", new LxRule([], Connective: null, 0), 0)
            ),
            Block.Runs => ReadRuns(line, c, new LxRuns("X", "X", 0)),
            _ => null,
        };

    private static object? ReadMachine(LexLine line, Cursor c, LxMachine m)
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
                return ReadSystem(line, c, m, n);
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
                return AddMigration(c, m, n);
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
                return NewState(c, m, n);
            case "Trigger:":
                return NewTrigger(c, m, n);
            case "Guard:":
                return NewGuard(c, m, n);
            case "Runs:":
                return NewRuns(c, m, n);
            case "Link:":
                return AddLink(c, m, n);
            case "Rule:":
                return AddRule(c, m, n);
            case "Reduction:":
                return AddReduction(c, m, n);
        }
        throw new InvalidOperationException($"{line.Keyword.Text} has no reader under Machine:.");
    }

    private static object? ReadSystem(LexLine line, Cursor c, LxMachine m, int n)
    {
        if (m.SystemLine != 0)
            throw Again("System", m.SystemLine);
        if (string.Equals(line.Keyword.Text, "System at:", StringComparison.Ordinal))
            m.SystemAt = c.Word("the system key");
        if (string.Equals(line.Keyword.Text, "System keyed by:", StringComparison.Ordinal))
            m.KeyedBy = c.Words("a key part");
        m.SystemLine = n;
        return null;
    }

    private static object? AddMigration(Cursor c, LxMachine m, int n)
    {
        var from = c.Integer();
        c.Expect("with");
        m.Migrations.Add(new LxMigration(from, c.Word("the migration's type"), n));
        return null;
    }

    private static LxState NewState(Cursor c, LxMachine m, int n)
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

    private static LxTrigger NewTrigger(Cursor c, LxMachine m, int n)
    {
        var trigger = new LxTrigger(
            c.Word("the trigger's name"),
            c.Text("the trigger's phrase"),
            n
        );
        m.Triggers.Add(trigger);
        return trigger;
    }

    private static LxGuard NewGuard(Cursor c, LxMachine m, int n)
    {
        var name = c.Word("the guard's name");
        c.Expect("when");
        var guard = new LxGuard(name, c.Rule(), n);
        m.Guards.Add(guard);
        return guard;
    }

    private static LxRuns NewRuns(Cursor c, LxMachine m, int n)
    {
        var train = c.Word("the train");
        c.Expect("in");
        var runs = new LxRuns(train, c.Word("the state it runs in"), n);
        m.Runs.Add(runs);
        return runs;
    }

    private static object? AddLink(Cursor c, LxMachine m, int n)
    {
        var phrase = c.Text("the link's phrase");
        c.Expect("to");
        var machine = c.Word("a machine");
        c.Expect("by");
        m.Links.Add(new LxLink(phrase, machine, c.Word("a context field"), n));
        return null;
    }

    private static object? AddRule(Cursor c, LxMachine m, int n)
    {
        var name = c.Word("the rule's name");
        c.Expect("with");
        var type = c.Word("the rule's type");
        c.Comma();
        c.Expect("example");
        var example = c.Next("true or false");
        if (!example.Is("true") && !example.Is("false"))
            throw c.Form("a rule's example is true or false");
        m.Rules.Add(new LxHandler(name, type, example.Is("true"), ReductionExample: null, n));
        return null;
    }

    private static object? AddReduction(Cursor c, LxMachine m, int n)
    {
        var name = c.Word("the reduction's name");
        c.Expect("with");
        var type = c.Word("the reduction's type");
        c.Comma();
        c.Expect("example");
        m.Reductions.Add(new LxHandler(name, type, RuleExample: null, c.Assignments(), n));
        return null;
    }

    private static object? ReadState(LexLine line, Cursor c, LxState state)
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

    private static object? ReadTrigger(LexLine line, Cursor c, LxTrigger trigger)
    {
        var n = line.Number;
        void Reduce(LxReduce reduce)
        {
            if (trigger.Reduce is { } earlier)
                throw new FormErrorException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"a trigger reduces once, and line {earlier.Line} already does: one of Sets:, Keeps, Clears, "
                    ) + "Resets or Reduces with:."
                );
            trigger.Reduce = reduce;
        }
        switch (line.Keyword.Text)
        {
            case "Input:":
                trigger.Input.Add(c.Field(partial: false));
                return null;
            case "Sets:":
                Reduce(new LxReduce("set", c.Sets(), Name: null, n));
                return null;
            case "Keeps":
                Reduce(new LxReduce("keep", Steps: null, Name: null, n));
                return null;
            case "Clears":
                Reduce(new LxReduce("clear", Steps: null, Name: null, n));
                return null;
            case "Resets":
                Reduce(new LxReduce("reset", Steps: null, Name: null, n));
                return null;
            case "Reduces with:":
                Reduce(new LxReduce("custom", Steps: null, c.Word("a reduction"), n));
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

    private static object? ReadGuard(LexLine line, Cursor c, LxGuard guard)
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

    private static object? ReadRuns(LexLine line, Cursor c, LxRuns runs)
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
                    throw new FormErrorException(
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
                return AddOutcome(c, runs, n);
        }
        throw new InvalidOperationException($"{line.Keyword.Text} has no reader under Runs:.");
    }

    private static object? AddOutcome(Cursor c, LxRuns runs, int n)
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
            runs.Outcomes.Add(new LxOutcome(phrase, kind, When: null, n));
        }
        else
            runs.Outcomes.Add(new LxOutcome(phrase, "done", c.Rule(), n));
        return null;
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
            if ((long)values.Count * tails.Count > MaxValues)
                throw c.Form($"the two ranges make more than {MaxValues} values");
            values = values.SelectMany(v => tails.Select(t => v + t)).ToList();
        }
        return values;
    }

    /// <summary>The most values a <c>Values:</c> line gives, ranges multiplied.</summary>
    internal const int MaxValues = 1000;

    private static List<string> Range(Cursor c)
    {
        var from = c.Word("the first of a range");
        c.Expect("to");
        var to = c.Word("the last of a range");
        if (
            long.TryParse(from, System.Globalization.CultureInfo.InvariantCulture, out var a)
            && long.TryParse(to, System.Globalization.CultureInfo.InvariantCulture, out var b)
            && a <= b
        )
        {
            if (b - a >= MaxValues)
                throw c.Form($"'{from} to {to}' is more than {MaxValues} values");
            var numbers = new List<string>();
            for (var i = a; i <= b; i++)
                numbers.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return numbers;
        }
        if (
            from.Length == 1
            && to.Length == 1
            && Letter(from[0])
            && Letter(to[0])
            && from[0] <= to[0]
            && char.IsUpper(from[0]) == char.IsUpper(to[0])
        )
            return Enumerable
                .Range(from[0], to[0] - from[0] + 1)
                .Select(i => ((char)i).ToString())
                .ToList();
        throw c.Form(
            $"'{from} to {to}' is not a range: two letters from A to Z (or a to z), or two whole numbers, the first "
                + "not after the last"
        );
    }

    private static bool Letter(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');

    private static FormErrorException Again(string keyword, int line) =>
        new(
            string.Create(
                CultureInfo.InvariantCulture,
                $"'{keyword}' is already given at line {line}, and is given once."
            )
        );
}
