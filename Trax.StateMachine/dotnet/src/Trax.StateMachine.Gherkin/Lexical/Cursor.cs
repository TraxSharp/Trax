namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>
/// Reads the tokens after one line's keyword. Every read that does not find what the form says throws a
/// <see cref="FormError"/> naming the keyword's form, so a line is either read whole or is a problem.
/// </summary>
internal sealed class Cursor(LexLine line)
{
    private readonly IReadOnlyList<Token> _tokens = line.Tokens;
    private int _at;

    private static readonly Token None = new(TokenKind.Word, "");

    internal int Line => line.Number;

    internal bool AtEnd => _at >= _tokens.Count;

    internal Token Peek => AtEnd ? None : _tokens[_at];

    internal Token PeekAt(int ahead) => _at + ahead < _tokens.Count ? _tokens[_at + ahead] : None;

    internal FormError Form(string? detail = null) =>
        new(
            $"'{line.Keyword.Text}' reads '{line.Keyword.Form}'"
                + (detail is null ? "." : $": {detail}.")
        );

    internal Token Next(string what)
    {
        if (AtEnd)
            throw Form($"{what} is missing at the end of the line");
        return _tokens[_at++];
    }

    internal void End()
    {
        if (!AtEnd)
            throw Form($"'{Peek}' is more than the line says");
    }

    internal string Word(string what)
    {
        var token = Next(what);
        if (token.Kind != TokenKind.Word)
            throw Form($"'{token}' is where {what} goes, unquoted");
        return token.Value;
    }

    internal string Text(string what)
    {
        var token = Next(what);
        if (token.Kind != TokenKind.Text)
            throw Form($"'{token}' is where {what} goes, in quotes");
        return token.Value;
    }

    internal void Expect(string word)
    {
        var token = Next($"'{word}'");
        if (!token.Is(word))
            throw Form($"'{token}' is where '{word}' goes");
    }

    internal void Comma()
    {
        var token = Next("a comma");
        if (token.Kind != TokenKind.Comma)
            throw Form($"'{token}' is where a comma goes");
    }

    internal bool TryComma()
    {
        if (Peek.Kind != TokenKind.Comma || AtEnd)
            return false;
        _at++;
        return true;
    }

    internal int Integer()
    {
        var token = Next("a whole number");
        if (
            !LexiconSyntax.Number(token, out var number)
            || !double.IsInteger(number)
            || number < int.MinValue
            || number > int.MaxValue
        )
            throw Form($"'{token}' is where a whole number goes");
        return (int)number;
    }

    internal List<string> Words(string what)
    {
        var words = new List<string> { Word(what) };
        while (TryComma())
            words.Add(Word(what));
        return words;
    }

    /// <summary>
    /// A value: a quoted text, a number, <c>true</c>, <c>false</c>, <c>nothing</c> (null), a list in brackets, or a
    /// bare name (a constant, or a field of what the line reads from).
    /// </summary>
    /// <summary>The deepest a list in a value nests.</summary>
    internal const int MaxDepth = 32;

    internal LxValue Value() => Value(0);

    private LxValue Value(int depth)
    {
        var token = Next("a value");
        switch (token.Kind)
        {
            case TokenKind.Text:
                return new LxText(token.Value, Line);
            case TokenKind.Open:
                if (depth >= MaxDepth)
                    throw new FormError($"a list nests deeper than {MaxDepth}.");
                var items = new List<LxValue>();
                if (Peek.Kind == TokenKind.Close && !AtEnd)
                {
                    _at++;
                    return new LxList(items, Line);
                }
                do items.Add(Value(depth + 1));
                while (TryComma());
                var close = Next("']'");
                if (close.Kind != TokenKind.Close)
                    throw Form($"'{close}' is where ']' closes the list");
                return new LxList(items, Line);
            case TokenKind.Word:
                if (token.Is("true") || token.Is("false"))
                    return new LxBool(token.Is("true"), Line);
                if (token.Is("nothing"))
                    return new LxNull(Line);
                if (LexiconSyntax.Number(token, out var number))
                    return new LxNumber(number, Line);
                return new LxRef(token.Value, Line);
            default:
                throw Form($"'{token}' is where a value goes");
        }
    }

    /// <summary><c>field is value, field is value</c>.</summary>
    internal List<LxAssign> Assignments()
    {
        var assigns = new List<LxAssign>();
        do
        {
            var field = Word("a field");
            Expect("is");
            assigns.Add(new LxAssign(field, Value(), Line));
        } while (TryComma());
        return assigns;
    }

    /// <summary><c>field to value, field</c>: a bare field is set from the field of the same name.</summary>
    internal List<LxSet> Sets()
    {
        var sets = new List<LxSet>();
        do
        {
            var field = Word("a context field");
            if (Peek.Is("to"))
            {
                Expect("to");
                sets.Add(new LxSet(field, Value(), Line));
            }
            else
                sets.Add(new LxSet(field, new LxRef(field, Line), Line));
        } while (TryComma());
        return sets;
    }

    /// <summary>
    /// One field: <c>name is a type</c> and its clauses (<c>may be empty</c>, <c>at least N</c>, <c>at most N</c>,
    /// <c>one of "a", "b"</c>, and last <c>where &lt;rule&gt;</c>), or with <paramref name="partial"/>,
    /// <c>name may be empty</c>.
    /// </summary>
    internal LxField Field(bool partial)
    {
        var field = OneField(partial, many: false);
        return field;
    }

    /// <summary>Several fields on one line, each <c>name is a type</c> with its clauses, after commas.</summary>
    internal List<LxField> Fields()
    {
        var fields = new List<LxField> { OneField(partial: false, many: true) };
        while (TryComma())
            fields.Add(OneField(partial: false, many: true));
        return fields;
    }

    private static readonly HashSet<string> ClauseStarts = ["may", "at", "one", "where"];

    private LxField OneField(bool partial, bool many)
    {
        var name = Word("the field's name");
        if (partial && Peek.Is("may"))
        {
            MayBeEmpty();
            return new LxField(name, null, true, null, null, null, null, Line);
        }
        Expect("is");
        var type = Type();
        var mayBeEmpty = false;
        double? minimum = null;
        double? maximum = null;
        List<string>? values = null;
        LxRule? where = null;
        // A comma followed by a clause word goes on with this field; on a line of several fields, anything else after
        // a comma is the next field, and is left for the caller.
        while (Peek.Kind == TokenKind.Comma && !AtEnd && ClauseStarts.Contains(PeekAt(1).Value))
        {
            _at++;
            if (Peek.Is("may"))
            {
                MayBeEmpty();
                mayBeEmpty = true;
            }
            else if (Peek.Is("at"))
            {
                Expect("at");
                var most = Peek.Is("most");
                Expect(most ? "most" : "least");
                var bound = Number();
                if (most)
                    maximum = bound;
                else
                    minimum = bound;
            }
            else if (Peek.Is("one"))
            {
                Expect("one");
                Expect("of");
                values = [Text("a value")];
                while (Peek.Kind == TokenKind.Comma && PeekAt(1).Kind == TokenKind.Text)
                {
                    _at++;
                    values.Add(Text("a value"));
                }
            }
            else
            {
                Expect("where");
                where = Rule();
            }
        }
        if (!many && !AtEnd)
            throw Form(
                $"'{Peek}' is not a clause of a field: may be empty, at least, at most, one of, where"
            );
        return new LxField(name, type, mayBeEmpty, minimum, maximum, values, where, Line);
    }

    private void MayBeEmpty()
    {
        Expect("may");
        Expect("be");
        Expect("empty");
    }

    private double Number()
    {
        var token = Next("a number");
        if (!LexiconSyntax.Number(token, out var number))
            throw Form($"'{token}' is where a number goes");
        return number;
    }

    /// <summary>
    /// <c>a string</c>, <c>an integer</c>, <c>a uuid</c>, <c>a Square</c>, <c>a list of strings</c>,
    /// <c>a map of string to number</c>, <c>a list</c>, <c>an object</c>.
    /// </summary>
    internal LxTypeRef Type()
    {
        var article = Next("'a' or 'an'");
        if (!article.Is("a") && !article.Is("an"))
            throw Form($"'{article}' is where 'a' or 'an' and a type go");
        var name = Word("a type");
        if (name == "list")
        {
            if (!Peek.Is("of"))
                return new LxTypeRef(LxTypeRef.Simple, "array");
            Expect("of");
            return new LxTypeRef(LxTypeRef.List, "array", Element(Word("the type of its items")));
        }
        if (name == "map")
        {
            Expect("of");
            var key = Element(Word("the type of its keys"));
            Expect("to");
            return new LxTypeRef(
                LxTypeRef.Map,
                "object",
                Element(Word("the type of its values")),
                key
            );
        }
        return Element(name);
    }

    // A type named inside a list or a map may be plural: "strings", "Squares", "Addresses".
    // A named type keeps the name as written; the elaborator finds "Squares" as "Square".
    private static LxTypeRef Element(string name) =>
        SimpleTypes.Contains(name) ? new(LxTypeRef.Simple, name)
        : name.EndsWith('s') && SimpleTypes.Contains(name[..^1]) ? new(LxTypeRef.Simple, name[..^1])
        : new(LxTypeRef.Named, name);

    internal static readonly HashSet<string> SimpleTypes =
    [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "uuid",
        "date-time",
        "date-time-offset",
        "date",
        "time",
        "duration",
        "uri",
        "char",
    ];

    /// <summary>
    /// A rule, flat: atoms joined by one connective, <c>and</c> or <c>or</c>. A line with both is a problem; a rule
    /// that needs both is a custom rule written in C#.
    /// </summary>
    internal LxRule Rule()
    {
        var atoms = new List<LxAtom> { Atom() };
        string? connective = null;
        while (Peek.Is("and") || Peek.Is("or"))
        {
            var word = Next("and").Value;
            if (connective is not null && connective != word)
                throw new FormError(
                    "a rule joins its parts with 'and' or with 'or', never both on one line. A rule that needs both "
                        + "is a custom rule, written in C# and named here."
                );
            connective = word;
            atoms.Add(Atom());
        }
        return new LxRule(atoms, connective, Line);
    }

    private LxAtom Atom()
    {
        if (PeekAt(1).Is("holds"))
        {
            var name = Word("a custom rule");
            Expect("holds");
            return new LxAtom("custom", null, name, Line, Name: name);
        }
        string? source = null;
        if (
            (Peek.Is("input") || Peek.Is("context"))
            && PeekAt(1).Kind == TokenKind.Word
            && !PeekAt(1).Is("is")
            && !PeekAt(1).Is("has")
        )
            source = Next("input").Value;
        var field = Word("a field");
        var verb = Next("'is' or 'has'");
        if (verb.Is("has"))
        {
            var (op, count) = Compare(Peek.Is("fewer") ? "fewer" : null);
            var items = Word("'items'");
            if (items is not ("items" or "item"))
                throw Form($"'{items}' is where 'items' goes");
            return new LxAtom("count", source, field, Line, op, new LxNumber(count, Line));
        }
        if (!verb.Is("is"))
            throw Form($"'{verb}' is where 'is' or 'has' goes in a rule");
        var next = Peek;
        if (next.Is("present") || next.Is("absent"))
        {
            _at++;
            return new LxAtom(next.Value, source, field, Line);
        }
        if (next.Is("not"))
        {
            Expect("not");
            Expect("empty");
            return new LxAtom("nonEmpty", source, field, Line);
        }
        if (next.Is("a") || next.Is("an"))
        {
            var type = Type();
            if (type.Kind == LxTypeRef.List && type.Element!.Kind == LxTypeRef.Simple)
                return new LxAtom(
                    "arrayOf",
                    source,
                    field,
                    Line,
                    Type: JsonType(type.Element.Name)
                );
            if (
                type.Kind == LxTypeRef.Simple
                && type.Name
                    is "string"
                        or "number"
                        or "integer"
                        or "boolean"
                        or "array"
                        or "object"
            )
                return new LxAtom("ofType", source, field, Line, Type: type.Name);
            throw Form(
                $"'{type}' is not a type a rule checks: a string, a number, an integer, a boolean, a list, an object, or a list of one of them"
            );
        }
        if (next.Is("one"))
        {
            Expect("one");
            Expect("of");
            var values = new List<string> { Text("a value") };
            while (Peek.Kind == TokenKind.Comma && PeekAt(1).Kind == TokenKind.Text)
            {
                _at++;
                values.Add(Text("a value"));
            }
            return new LxAtom("oneOf", source, field, Line, Values: values);
        }
        if (next.Is("true") || next.Is("false") || next.Kind == TokenKind.Text)
            return new LxAtom("equals", source, field, Line, Value: Value());
        var (comparison, bound) = Compare(null);
        if (Peek.Is("characters"))
        {
            Expect("characters");
            Expect("long");
            return new LxAtom("length", source, field, Line, comparison, new LxNumber(bound, Line));
        }
        return new LxAtom("compare", source, field, Line, comparison, new LxNumber(bound, Line));
    }

    // "at least 3", "at most 3", "more than 3", "less than 3", "fewer than 3", "3".
    private (string Op, double Bound) Compare(string? fewer)
    {
        string op;
        if (Peek.Is("at"))
        {
            Expect("at");
            op = Peek.Is("most") ? "lte" : "gte";
            Expect(op == "lte" ? "most" : "least");
        }
        else if (Peek.Is("more"))
        {
            Expect("more");
            Expect("than");
            op = "gt";
        }
        else if (Peek.Is("less") || Peek.Is("fewer"))
        {
            Expect(fewer ?? (Peek.Is("less") ? "less" : "fewer"));
            Expect("than");
            op = "lt";
        }
        else
            op = "eq";
        var token = Next("a number");
        if (!LexiconSyntax.Number(token, out var number))
            throw Form(
                $"'{token}' does not finish a rule: present, absent, not empty, a <type>, one of \"…\", true, "
                    + "false, \"<text>\", a number, at least, at most, more than, less than, or 'has <n> items'"
            );
        return (op, number);
    }

    internal static string JsonType(string name) =>
        name switch
        {
            "string" or "number" or "integer" or "boolean" or "object" => name,
            "strings" => "string",
            "numbers" => "number",
            "integers" => "integer",
            "booleans" => "boolean",
            "objects" => "object",
            _ => "string",
        };
}
