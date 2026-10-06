using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Core.Decisions;

namespace Trax.Effect.Data.Decisions;

/// <summary>
/// The stored form of questions and answers in <c>trax.decision</c>: plain JSON with a
/// <c>type</c> discriminator, readable in SQL and independent of .NET type names.
/// </summary>
/// <remarks>
/// JSON has no NaN or infinity, and a shadow's answer is never checked the way the live one is, so
/// a number that is not finite is written as the string <c>"NaN"</c>, <c>"Infinity"</c> or
/// <c>"-Infinity"</c> and read back as the number. Each shadow is written on its own: one that
/// cannot be written is recorded with the reason in its <c>error</c>, and never costs the live
/// decision its record.
/// </remarks>
internal static class DecisionJson
{
    public static string Kind(Question question) =>
        question switch
        {
            ChoiceQuestion => "choice",
            ScoreQuestion => "score",
            YesNoQuestion => "yes_no",
            _ => question.GetType().Name,
        };

    public static string Write(Question question)
    {
        var node = new JsonObject
        {
            ["type"] = Kind(question),
            ["key"] = question.Key,
            ["instructions"] = question.Instructions,
        };

        switch (question)
        {
            case ChoiceQuestion choice:
                node["options"] = Criteria(choice.Options);
                break;
            case ScoreQuestion score:
                node["levels"] = Criteria(score.Levels);
                break;
            case YesNoQuestion yesNo:
                node["yes"] = yesNo.Yes;
                node["no"] = yesNo.No;
                break;
        }

        return node.ToJsonString();
    }

    public static string Write(Answer answer) => Node(answer).ToJsonString();

    /// <summary>
    /// The answer as stored, with why an earlier run's answer to the same question was not
    /// replayed, when it was not.
    /// </summary>
    public static string Write(Answer answer, string? replayRefused)
    {
        var node = Node(answer);

        if (replayRefused is not null)
            node["replay_refused"] = replayRefused;

        return node.ToJsonString();
    }

    /// <summary>
    /// The routings stored on a decision with one more added after them: a JSON array of
    /// <c>{"track", "fallback_reason"}</c> in the order the steps took them.
    /// </summary>
    public static string AddRoute(string? routes, string track, string? fallbackReason)
    {
        var array = routes is null
            ? new JsonArray()
            : JsonNode.Parse(routes) as JsonArray
                ?? throw new JsonException("A decision's routes are not a JSON array.");

        array.Add(new JsonObject { ["track"] = track, ["fallback_reason"] = fallbackReason });
        return array.ToJsonString();
    }

    public static string? Write(IReadOnlyList<ShadowAnswer> shadows) =>
        shadows.Count == 0 ? null : new JsonArray(shadows.Select(Shadow).ToArray()).ToJsonString();

    private static JsonNode Shadow(ShadowAnswer shadow)
    {
        try
        {
            var node = new JsonObject
            {
                ["decider"] = shadow.Decider.FullName,
                ["agrees"] = shadow.Agrees,
                ["error"] = shadow.Error,
                ["answer"] = shadow.Answer is null ? null : Node(shadow.Answer),
            };

            // Written here so a shadow that cannot be written fails alone, not the whole array.
            return JsonNode.Parse(node.ToJsonString())!;
        }
        catch (Exception e)
        {
            return new JsonObject
            {
                ["decider"] = shadow.Decider?.FullName,
                ["agrees"] = shadow.Agrees,
                ["error"] = $"its answer could not be recorded: {e.Message}",
                ["answer"] = null,
            };
        }
    }

    /// <summary>
    /// Reads a stored answer back, or throws <see cref="JsonException"/> when it cannot be: a replay
    /// that cannot read what it recorded must not quietly ask again. However a stored answer is
    /// damaged (not JSON, a field of the wrong kind, a number that is not one), the failure is a
    /// <see cref="JsonException"/>, so the caller has one thing to catch.
    /// </summary>
    public static Answer ReadAnswer(string json)
    {
        try
        {
            return Read(json);
        }
        catch (Exception e) when (e is not JsonException)
        {
            throw new JsonException($"A recorded answer cannot be read: {e.Message}", e);
        }
    }

    private static Answer Read(string json)
    {
        var node =
            JsonNode.Parse(json) as JsonObject
            ?? throw new JsonException("A recorded answer is not a JSON object.");

        var model = node["model"]?.GetValue<string>();

        Answer answer = (string?)node["type"] switch
        {
            "choice" => new ChoiceAnswer(
                Required(node, "choice").GetValue<string>(),
                ReadNumber(Required(node, "confidence")),
                node["probabilities"]?.AsObject().ToDictionary(p => p.Key, p => ReadNumber(p.Value))
            ),
            "score" => new ScoreAnswer(
                ReadNumber(Required(node, "score")),
                ReadNumber(Required(node, "confidence")),
                node["probabilities"]?.AsArray().Select(ReadNumber).ToList()
            ),
            "yes_no" => new YesNoAnswer(ReadNumber(Required(node, "probability"))),
            var other => throw new JsonException(
                $"A recorded answer has the unknown type '{other}'."
            ),
        };

        return answer with
        {
            Model = model,
        };
    }

    private static JsonObject Node(Answer answer)
    {
        var node = answer switch
        {
            ChoiceAnswer c => new JsonObject
            {
                ["type"] = "choice",
                ["choice"] = c.Choice,
                ["confidence"] = Number(c.Confidence),
                ["probabilities"] = c.Probabilities is null
                    ? null
                    : new JsonObject(
                        c.Probabilities.Select(p =>
                            KeyValuePair.Create(p.Key, (JsonNode?)Number(p.Value))
                        )
                    ),
            },
            ScoreAnswer s => new JsonObject
            {
                ["type"] = "score",
                ["score"] = Number(s.Score),
                ["confidence"] = Number(s.Confidence),
                ["probabilities"] = s.Probabilities is null
                    ? null
                    : new JsonArray(s.Probabilities.Select(p => (JsonNode?)Number(p)).ToArray()),
            },
            YesNoAnswer y => new JsonObject
            {
                ["type"] = "yes_no",
                ["probability"] = Number(y.Probability),
            },
            // Written as a bare type name, it could never be read back, so a replay would find
            // an answer it cannot honour. Refused here, where the decision is still being made.
            _ => throw new NotSupportedException(
                $"An answer of type '{answer.GetType().FullName}' cannot be recorded; only "
                    + "ChoiceAnswer, ScoreAnswer and YesNoAnswer can."
            ),
        };

        node["model"] = answer.Model;
        return node;
    }

    private static JsonArray Criteria(IReadOnlyList<Criterion> criteria) =>
        new(
            criteria
                .Select(c =>
                    (JsonNode)new JsonObject { ["name"] = c.Name, ["description"] = c.Description }
                )
                .ToArray()
        );

    /// <summary>A number as JSON, or its name as a string when JSON cannot hold it.</summary>
    private static JsonNode Number(double value) =>
        double.IsFinite(value)
            ? JsonValue.Create(value)
            : JsonValue.Create(value.ToString(CultureInfo.InvariantCulture));

    private static double ReadNumber(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
            return double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var number
            )
                ? number
                : throw new JsonException(
                    $"A recorded answer has '{text}' where a number belongs."
                );

        return node?.GetValue<double>()
            ?? throw new JsonException("A recorded answer has a number that is null.");
    }

    private static JsonNode Required(JsonObject node, string name) =>
        node[name] ?? throw new JsonException($"A recorded answer has no '{name}'.");
}
