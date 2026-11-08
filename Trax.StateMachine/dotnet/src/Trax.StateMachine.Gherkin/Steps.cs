using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>What one step means. A user machine's instance is named by its owner; a system machine's has none.</summary>
internal abstract record Meaning
{
    /// <summary><c>Ada is a customer</c>: names a person for the scenario.</summary>
    internal sealed record Named(string Name, string Actor) : Meaning;

    /// <summary><c>Ada has an order</c>: a person starts an instance of their own.</summary>
    internal sealed record Start(string Person, string Machine) : Meaning;

    /// <summary><c>Ada places her order</c>: a person fires a trigger.</summary>
    internal sealed record Act(
        string Person,
        string Machine,
        string? Owner,
        string Trigger,
        JsonObject? Input
    ) : Meaning;

    /// <summary><c>Ada's order starts baking</c>, <c>the shop opens</c>: a trigger no person fires.</summary>
    internal sealed record Happens(
        string Machine,
        string? Owner,
        string Trigger,
        JsonObject? Input,
        string? Key = null
    ) : Meaning;

    /// <summary><c>Ada's order is placed</c>, or with <see cref="Still"/>, <c>is still a draft</c>.</summary>
    internal sealed record InState(
        string Machine,
        string? Owner,
        string State,
        bool Still,
        string? Key = null
    ) : Meaning;

    /// <summary><c>Ada's order has nothing in it</c>: a guard holds or fails.</summary>
    internal sealed record Guard(
        string Machine,
        string? Owner,
        string Name,
        bool Holds,
        string? Key = null
    ) : Meaning;

    /// <summary><c>the payment goes through</c>: an invoked train's outcome.</summary>
    internal sealed record Outcome(string Machine, string State, string Phrase) : Meaning;

    /// <summary><c>Ada is told "Add something first."</c></summary>
    internal sealed record Told(string Person, string Message) : Meaning;

    /// <summary><c>Ada is not allowed</c></summary>
    internal sealed record Denied(string Person) : Meaning;

    /// <summary><c>5 minutes pass</c></summary>
    internal sealed record Passes(TimeSpan Time) : Meaning;

    /// <summary><c>Ada's orders show status "In the oven"</c>: a person reads one field of a view.</summary>
    internal sealed record ViewShows(string Person, string View, string Field, string Value)
        : Meaning;
}

/// <summary>
/// Resolves a step's text to its <see cref="Meaning"/>, from the glossary's phrases and the fixed English steps (ADR
/// 0058). A person is a capitalised name the scenario introduced with <c>Name is &lt;noun&gt;</c>; a
/// user machine's instance is <c>Name's noun</c>, or <c>her</c>, <c>his</c> or <c>their noun</c> for the step's own
/// person. A value is quoted and read as its field's type. A step two meanings match is ambiguous, never the first.
/// </summary>
internal sealed partial class StepResolver
{
    private const string NamePattern = @"(?-i:[A-Z][a-z][A-Za-z]*)";

    private readonly Glossary _glossary;
    private readonly List<(Regex Pattern, Func<Match, Meaning> Read)> _patterns = [];

    internal StepResolver(Glossary glossary)
    {
        _glossary = glossary;
        foreach (var actor in glossary.Actors.Values)
            Add(
                $@"(?<person>{NamePattern})\s+is\s+{Words(actor.Noun)}",
                m => new Meaning.Named(m.Groups["person"].Value, actor.Name)
            );
        Add(
            $@"(?<person>{NamePattern})\s+is\s+told\s+""(?<message>[^""]*)""",
            m => new Meaning.Told(m.Groups["person"].Value, m.Groups["message"].Value)
        );
        Add(
            $@"(?<person>{NamePattern})\s+is\s+not\s+allowed",
            m => new Meaning.Denied(m.Groups["person"].Value)
        );
        Add(
            @"(?<n>\d{1,9})\s+(?<unit>seconds?|minutes?|hours?|days?)\s+pass(?:es)?",
            m => new Meaning.Passes(
                Time(
                    int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture),
                    m.Groups["unit"].Value
                )
            )
        );

        foreach (var machine in glossary.Machines.Values)
        {
            var reference = Reference(machine);
            if (machine.Start is { } start)
                Add(
                    $@"(?<person>{NamePattern})\s+{Words(start)}",
                    m => new Meaning.Start(m.Groups["person"].Value, machine.Id)
                );
            foreach (var (state, phrase) in machine.States)
            {
                Add(
                    $@"{reference}\s+{Words(phrase)}",
                    m => new Meaning.InState(machine.Id, Owner(m), state, false, KeyOf(m))
                );
                Add(
                    $@"{reference}\s+{Still(phrase)}",
                    m => new Meaning.InState(machine.Id, Owner(m), state, true, KeyOf(m))
                );
            }
            foreach (var guard in machine.Guards.Values)
            {
                Add(
                    $@"{reference}\s+{Words(guard.Holds)}",
                    m => new Meaning.Guard(machine.Id, Owner(m), guard.Name, true, KeyOf(m))
                );
                Add(
                    $@"{reference}\s+{Words(guard.Fails)}",
                    m => new Meaning.Guard(machine.Id, Owner(m), guard.Name, false, KeyOf(m))
                );
            }
            foreach (var trigger in machine.Triggers.Values)
            {
                var template = Template(trigger.Phrase, reference);
                Add(
                    $@"(?<person>{NamePattern})\s+{template}",
                    m => new Meaning.Act(
                        m.Groups["person"].Value,
                        machine.Id,
                        Owner(m),
                        trigger.Name,
                        Inputs(m, trigger)
                    )
                );
                Add(
                    template,
                    m => new Meaning.Happens(
                        machine.Id,
                        Owner(m),
                        trigger.Name,
                        Inputs(m, trigger),
                        KeyOf(m)
                    )
                );
            }
            foreach (var invoke in machine.Invokes.Values)
            foreach (var phrase in invoke.Outcomes.Keys)
                Add(Words(phrase), _ => new Meaning.Outcome(machine.Id, invoke.State, phrase));
        }

        // A person reading one field of a view, the field named: "Ada's orders show status "In the oven"".
        foreach (var view in glossary.Views.Values)
        foreach (var field in view.Fields)
            Add(
                $@"(?<person>{NamePattern})'s\s+{Words(view.Name)}\s+shows?\s+{Words(field.Name)}\s+""(?<value>[^""]*)""",
                m => new Meaning.ViewShows(
                    m.Groups["person"].Value,
                    view.Name,
                    field.Name,
                    m.Groups["value"].Value
                )
            );
    }

    /// <summary>
    /// The meaning of <paramref name="text"/>, or why it has none. <paramref name="unresolved"/> is true when no phrase
    /// matched at all, which a Then may be (a step about the UI).
    /// </summary>
    internal Meaning? Resolve(
        string text,
        IReadOnlyDictionary<string, string> people,
        out string? problem,
        out bool unresolved
    )
    {
        problem = null;
        unresolved = false;
        if (FirstPerson().IsMatch(text))
        {
            problem =
                $"'{text}' speaks as 'I'. Name the person: 'Given Ada is {_glossary.Actors.Values.First().Noun}', then 'Ada ...'.";
            return null;
        }

        var meanings = new List<Meaning>();
        foreach (var (pattern, read) in _patterns)
            if (pattern.Match(text) is { Success: true } match)
            {
                Meaning meaning;
                try
                {
                    meaning = read(match);
                }
                catch (FormatException ex)
                {
                    problem = ex.Message;
                    return null;
                }
                if (Pronoun(match) is { } pronoun && meaning is not Meaning.Act)
                {
                    problem =
                        $"'{pronoun}' names the step's own person, and '{text}' has none. Name the owner: 'Ada's ...'.";
                    return null;
                }
                meanings.Add(meaning);
            }

        if (meanings.Count == 0)
        {
            unresolved = true;
            problem = $"No phrase in the glossary says '{text}'.";
            return null;
        }
        if (meanings.Distinct().Count() > 1)
        {
            problem =
                $"'{text}' could mean {string.Join(" or ", meanings.Distinct().Select(Describe))}. Reword one phrase.";
            return null;
        }

        var resolved = meanings[0];
        // A person named in a step was introduced by the scenario, and a user's instance is named by a person whose
        // actor owns that machine.
        foreach (var name in Names(resolved))
            if (resolved is not Meaning.Named && !people.ContainsKey(name))
            {
                problem =
                    $"'{text}' names {name}, and the scenario never says who {name} is. Add 'Given {name} is {_glossary.Actors.Values.First().Noun}'.";
                return null;
            }
        if (
            OwnerOf(resolved) is ({ } machine, { } owner)
            && people.TryGetValue(owner, out var actor)
        )
        {
            var ownedBy = _glossary.Machines[machine].OwnedBy ?? [];
            if (!ownedBy.Contains(actor))
            {
                problem =
                    $"'{text}' names {owner}'s {_glossary.Machines[machine].Reference}, and {owner} is {_glossary.Actors[actor].Noun}: a {_glossary.Machines[machine].Reference} belongs to {string.Join(" or ", ownedBy.Select(o => _glossary.Actors[o].Noun))}.";
                return null;
            }
        }
        return resolved;
    }

    private static IEnumerable<string> Names(Meaning meaning) =>
        meaning switch
        {
            Meaning.Start s => [s.Person],
            Meaning.Act a => a.Owner is { } o ? [a.Person, o] : [a.Person],
            Meaning.Told t => [t.Person],
            Meaning.Denied d => [d.Person],
            Meaning.ViewShows v => [v.Person],
            _ => OwnerOf(meaning) is (_, { } owner) ? [owner] : [],
        };

    private static (string? Machine, string? Owner) OwnerOf(Meaning meaning) =>
        meaning switch
        {
            Meaning.Act a => (a.Machine, a.Owner),
            Meaning.Happens h => (h.Machine, h.Owner),
            Meaning.InState s => (s.Machine, s.Owner),
            Meaning.Guard g => (g.Machine, g.Owner),
            _ => (null, null),
        };

    private static string Describe(Meaning meaning) =>
        meaning switch
        {
            Meaning.Act a => $"{a.Person} firing {a.Machine}.{a.Trigger}",
            Meaning.Happens h => $"{h.Machine}.{h.Trigger}",
            Meaning.InState s => $"{s.Machine} in {s.State}",
            Meaning.Guard g => $"{g.Machine}'s guard {g.Name}",
            Meaning.Outcome o => $"{o.Machine}'s outcome '{o.Phrase}'",
            _ => meaning.GetType().Name.ToLowerInvariant(),
        };

    private void Add(string pattern, Func<Match, Meaning> read) =>
        _patterns.Add(
            (
                new Regex(
                    $@"\A{pattern}\z",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)
                ),
                read
            )
        );

    // A machine named in a step: a user's by its owner ("Ada's order") or the step's own person ("her order"), a system
    // machine by its name, and a keyed one by its name and each part of its key, quoted ('ticket "T-12"').
    private static string Reference(GlossaryMachine machine) =>
        machine.KeyedBy is { } parts
            ? Words(machine.Reference)
                + string.Concat(parts.Select((_, i) => $@"\s+""(?<key{i}>[^""]*)"""))
        : machine.SystemKey is null
            ? $@"(?:(?<owner>{NamePattern})'s|(?<pronoun>her|his|their))\s+{Words(machine.Reference)}"
        : Words(machine.Reference);

    // The key a step names a keyed system machine's instance by, as a JSON array of its parts; null when it names none.
    private static string? KeyOf(Match match)
    {
        if (!match.Groups["key0"].Success)
            return null;
        var parts = new JsonArray();
        for (var i = 0; match.Groups[$"key{i}"] is { Success: true } part; i++)
            parts.Add(part.Value);
        return parts.ToJsonString();
    }

    private static string? Owner(Match match) =>
        match.Groups["owner"] is { Success: true } owner ? owner.Value
        : match.Groups["pronoun"].Success && match.Groups["person"] is { Success: true } person
            ? person.Value
        : null;

    private static string? Pronoun(Match match) =>
        match.Groups["pronoun"] is { Success: true } pronoun && !match.Groups["person"].Success
            ? pronoun.Value
            : null;

    // A trigger's phrase: {subject} is the machine's reference and every other placeholder a quoted value, each word
    // or placeholder a token, any run of whitespace between them.
    private static string Template(string phrase, string reference)
    {
        var tokens = new List<string>();
        foreach (var word in phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var rest = word;
            var token = new StringBuilder();
            while (rest.IndexOf('{') is var open and >= 0)
            {
                var close = rest.IndexOf('}', open);
                var name = rest[(open + 1)..close];
                token.Append(Regex.Escape(rest[..open]));
                token.Append(
                    name == Glossary.SubjectSlot ? reference : $@"""(?<in_{name}>[^""]*)"""
                );
                rest = rest[(close + 1)..];
            }
            tokens.Add(token.Append(Regex.Escape(rest)).ToString());
        }
        return string.Join(@"\s+", tokens);
    }

    // Each quoted value of a step, read as its field's type; a field the step leaves unset is refused unless nullable.
    private static JsonObject? Inputs(Match match, GlossaryTrigger trigger)
    {
        // An effect's trigger is sent with no input: the effect runner fires it with the receipt.
        if (trigger.InputNames.Count == 0 || trigger.Effect is not null)
            return null;
        var input = new JsonObject();
        foreach (var field in trigger.Input)
        {
            var name = field!["name"]!.GetValue<string>();
            var type = field["type"]!.GetValue<string>();
            var nullable = field["nullable"]!.GetValue<bool>();
            if (match.Groups[$"in_{name}"] is not { Success: true } value)
            {
                if (!nullable)
                    throw new FormatException(
                        $"'{match.Value}' leaves '{name}' unset, and {trigger.Name} needs it. Add it to the phrase as {{{name}}}."
                    );
                input[name] = null;
                continue;
            }
            input[name] = Coerce(value.Value, type, name);
        }
        return input;
    }

    private static JsonNode? Coerce(string text, string type, string field) =>
        type switch
        {
            "string" => JsonValue.Create(text),
            "integer"
                when long.TryParse(
                    text,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out var i
                ) => JsonValue.Create(i),
            "number"
                when double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var d
                ) && double.IsFinite(d) => JsonValue.Create(d),
            "boolean" when text is "true" or "false" => JsonValue.Create(text == "true"),
            "array" or "object" => TryJson(text, type, field),
            _ => throw new FormatException(
                $"\"{text}\" is not {(type is "integer" ? "an" : "a")} {type}, which '{field}' is."
            ),
        };

    private static JsonNode? TryJson(string text, string type, string field)
    {
        try
        {
            var node = JsonNode.Parse(text);
            if ((type == "array" && node is JsonArray) || (type == "object" && node is JsonObject))
                return node;
        }
        catch (System.Text.Json.JsonException) { }
        throw new FormatException($"\"{text}\" is not a JSON {type}, which '{field}' is.");
    }

    // A wait no TimeSpan holds is refused as the step's problem, not thrown out of the derivation.
    private static TimeSpan Time(int n, string unit)
    {
        try
        {
            return unit.ToLowerInvariant().TrimEnd('s') switch
            {
                "second" => TimeSpan.FromSeconds(n),
                "minute" => TimeSpan.FromMinutes(n),
                "hour" => TimeSpan.FromHours(n),
                _ => TimeSpan.FromDays(n),
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new FormatException(
                $"'{n} {unit}' is longer than a clock holds ({TimeSpan.MaxValue.Days} days)."
            );
        }
    }

    // A phrase's words, each escaped, any run of whitespace between them.
    private static string Words(string phrase) =>
        string.Join(
            @"\s+",
            phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape)
        );

    // "is a draft" said as a stay: "is still a draft".
    private static string Still(string phrase)
    {
        var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(
            @"\s+",
            new[] { Regex.Escape(words[0]), "still" }.Concat(words.Skip(1).Select(Regex.Escape))
        );
    }

    [GeneratedRegex(@"\A(?:I|I'm|I've|my|me)\b|\s(?:my|me|I)\s", RegexOptions.CultureInvariant)]
    private static partial Regex FirstPerson();
}
