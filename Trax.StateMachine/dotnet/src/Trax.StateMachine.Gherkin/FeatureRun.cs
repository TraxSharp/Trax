using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// Which scenarios a run takes, as Cucumber selects them: a tag expression (<c>@smoke and not @slow</c>), name
/// patterns (any may match), and locations (<c>ordering.feature:12</c>, a scenario's line or an outline row's).
/// </summary>
internal sealed record RunSelection(
    string? Tags = null,
    IReadOnlyList<string>? Names = null,
    IReadOnlyList<(string File, long Line)>? Locations = null
);

/// <summary>A run: each scenario's result, in file and line order, and what stopped a file from being read.</summary>
internal sealed record RunReport(
    IReadOnlyList<ScenarioResult> Scenarios,
    IReadOnlyList<Problem> Problems
);

/// <summary>
/// Runs features on a directory of derived machines, in memory (ADR <c>Trax.Docs/adr/0058</c>). Every scenario gets a
/// fresh driver, so no scenario sees another's instances. A dry run reads every step and runs none.
/// </summary>
internal static class FeatureRun
{
    internal static RunReport Run(
        Glossary glossary,
        IReadOnlyList<(string Path, string Text)> features,
        IReadOnlyDictionary<string, JsonObject> irs,
        HostFile host,
        RunSelection selection,
        bool dryRun = false,
        bool failFast = false
    )
    {
        var (scenarios, problems) = FeatureFiles.Parse(features);
        var tags = selection.Tags is { } expression ? TagExpression.Parse(expression) : null;
        var names = selection
            .Names?.Select(n => new Regex(n, RegexOptions.None, TimeSpan.FromSeconds(1)))
            .ToList();
        var resolver = new StepResolver(glossary);
        var results = new List<ScenarioResult>();
        foreach (
            var scenario in scenarios
                .OrderBy(s => s.File, StringComparer.Ordinal)
                .ThenBy(s => s.Line)
        )
        {
            if (tags is not null && !tags(scenario.Tags))
                continue;
            if (names is { Count: > 0 } && !names.Any(n => n.IsMatch(scenario.Name)))
                continue;
            if (
                selection.Locations is { Count: > 0 } locations
                && !locations.Any(l =>
                    l.File == scenario.File
                    && (l.Line == scenario.Line || l.Line == scenario.OutlineLine)
                )
            )
                continue;

            if (scenario.HostOnly)
            {
                // It needs a handler only the host has, which no in-memory run loads.
                results.Add(
                    new ScenarioResult(
                        scenario,
                        ScenarioStatus.Skipped,
                        scenario.Steps.Select(s => new StepResult(s, null, "not run")).ToList()
                    )
                );
                continue;
            }
            var walk = Walk.Run(scenario, glossary, resolver);
            var result = dryRun
                ? DryRun(walk)
                : ScenarioRunner
                    .Run(walk, glossary, new MemoryDriver(irs, host, glossary))
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            results.Add(result);
            if (failFast && result.Status is not ScenarioStatus.Passed)
                break;
        }
        return new RunReport(results, problems);
    }

    // Every step read, none run: an undefined Given or When, or a step the walk refused, still shows.
    private static ScenarioResult DryRun(Walk walk)
    {
        var meanings = walk.Steps.Select(s => s.Step).ToHashSet();
        var steps = walk
            .Scenario.Steps.Select(step =>
                walk.Problems.FirstOrDefault(p => p.Line == step.Line) is { } problem
                    ? new StepResult(step, ScenarioStatus.Undefined, problem.Message)
                : meanings.Contains(step) ? new StepResult(step, null, "not run")
                : step.Kind == StepKind.Then
                    ? new StepResult(step, null, "about the UI: not checked here")
                : new StepResult(
                    step,
                    ScenarioStatus.Undefined,
                    walk.Problems.FirstOrDefault(p => p.Line == step.Line)?.Message
                )
            )
            .ToList();
        return new ScenarioResult(
            walk.Scenario,
            steps.Any(s => s.Status is ScenarioStatus.Undefined)
                ? ScenarioStatus.Undefined
                : ScenarioStatus.Passed,
            steps
        );
    }
}

/// <summary>
/// Cucumber's tag expressions: tags, <c>not</c>, <c>and</c>, <c>or</c> and parentheses, <c>not</c> binding tightest and
/// <c>or</c> loosest. <c>\</c> escapes a space or parenthesis in a tag.
/// </summary>
internal static class TagExpression
{
    /// <exception cref="FormatException">The expression does not parse; the message says where.</exception>
    internal static Func<IReadOnlyList<string>, bool> Parse(string expression)
    {
        var tokens = Tokens(expression);
        var at = 0;
        var parsed = Or();
        if (at < tokens.Count)
            throw new FormatException($"'{expression}': '{tokens[at]}' is not expected here.");
        return parsed;

        Func<IReadOnlyList<string>, bool> Or()
        {
            var left = And();
            while (at < tokens.Count && tokens[at] == "or")
            {
                at++;
                var l = left;
                var r = And();
                left = t => l(t) || r(t);
            }
            return left;
        }

        Func<IReadOnlyList<string>, bool> And()
        {
            var left = Not();
            while (at < tokens.Count && tokens[at] == "and")
            {
                at++;
                var l = left;
                var r = Not();
                left = t => l(t) && r(t);
            }
            return left;
        }

        Func<IReadOnlyList<string>, bool> Not()
        {
            if (at < tokens.Count && tokens[at] == "not")
            {
                at++;
                var inner = Not();
                return t => !inner(t);
            }
            return Atom();
        }

        Func<IReadOnlyList<string>, bool> Atom()
        {
            if (at >= tokens.Count)
                throw new FormatException($"'{expression}' ends where a tag is expected.");
            var token = tokens[at++];
            if (token == "(")
            {
                var inner = Or();
                if (at >= tokens.Count || tokens[at] != ")")
                    throw new FormatException($"'{expression}' has a '(' that is not closed.");
                at++;
                return inner;
            }
            if (token is ")" or "and" or "or")
                throw new FormatException($"'{expression}': '{token}' is not expected here.");
            if (!token.StartsWith('@'))
                throw new FormatException(
                    $"'{expression}': '{token}' is not a tag; a tag starts with @."
                );
            return t => t.Contains(token, StringComparer.Ordinal);
        }
    }

    private static List<string> Tokens(string expression)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        void Flush()
        {
            if (current.Length > 0)
                tokens.Add(current.ToString());
            current.Clear();
        }
        for (var i = 0; i < expression.Length; i++)
        {
            var c = expression[i];
            if (c == '\\' && i + 1 < expression.Length)
                current.Append(expression[++i]);
            else if (char.IsWhiteSpace(c))
                Flush();
            else if (c is '(' or ')')
            {
                Flush();
                tokens.Add(c.ToString());
            }
            else
                current.Append(c);
        }
        Flush();
        return tokens;
    }
}
