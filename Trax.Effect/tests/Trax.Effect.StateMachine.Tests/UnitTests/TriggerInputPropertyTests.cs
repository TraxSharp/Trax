using System.Text.Json.Nodes;
using AwesomeAssertions;
using CsCheck;
using Trax.Effect.StateMachine.Tests.Fakes;
using Trax.Effect.StateMachine.Tests.Helpers;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// Generated inputs for a trigger with a declared input record: a valid <see cref="RangeMachine.RangeInput"/>,
/// then at most one mutation that breaks it. The verdict is <c>INVALID_INPUT</c> exactly when a mutation was
/// applied. Every generated input is sent through the TypeScript twin, built from the machine's exported IR, as
/// well, and gets the same verdict there.
/// </summary>
[Property("adr", TriggerInputValidationTests.Adr)]
public class TriggerInputPropertyTests
{
    private const string Adr = TriggerInputValidationTests.Adr;

    public enum Mutation
    {
        None,
        DropField,
        ChangeKind,
        Stringify,
        AddField,
        PascalCase,
        Nest,
        Null,
        NonObject,
    }

    // The fields every valid input must carry; note is nullable, so it may be null but not of another kind.
    private static readonly string[] Required = ["from", "to", "tags"];

    private static readonly Gen<JsonObject> AValidInput = Gen.Select(
        Gen.Int[0, 10_000],
        Gen.Int,
        Gen.String[Gen.Char.AlphaNumeric, 0, 8].Null(),
        Gen.String[Gen.Char.AlphaNumeric, 1, 6].List[1, 4],
        (from, to, note, tags) =>
            new JsonObject
            {
                ["from"] = from,
                ["to"] = to,
                ["note"] = note,
                ["tags"] = new JsonArray(tags.Select(t => (JsonNode?)t).ToArray()),
            }
    );

    private static readonly Gen<Mutation> AMutation = Gen.Frequency(
        (4, Gen.Const(Mutation.None)),
        (1, Gen.Const(Mutation.DropField)),
        (1, Gen.Const(Mutation.ChangeKind)),
        (1, Gen.Const(Mutation.Stringify)),
        (1, Gen.Const(Mutation.AddField)),
        (1, Gen.Const(Mutation.PascalCase)),
        (1, Gen.Const(Mutation.Nest)),
        (1, Gen.Const(Mutation.Null)),
        (1, Gen.Const(Mutation.NonObject))
    );

    private static JsonNode? Apply(Mutation mutation, JsonObject input, int pick)
    {
        var field = Required[pick % Required.Length];
        var numeric = pick % 2 == 0 ? "from" : "to";
        switch (mutation)
        {
            case Mutation.None:
                return input;
            case Mutation.DropField:
                input.Remove(field);
                return input;
            case Mutation.ChangeKind:
                input[field] = field == "tags" ? "not-a-list" : true;
                return input;
            case Mutation.Stringify:
                input[numeric] = input[numeric]!.GetValue<int>().ToString();
                return input;
            case Mutation.AddField:
                input["extra"] = pick;
                return input;
            case Mutation.PascalCase:
                var value = input[field]!.DeepClone();
                input.Remove(field);
                input[char.ToUpperInvariant(field[0]) + field[1..]] = value;
                return input;
            case Mutation.Nest:
                input[field] = new JsonObject { ["value"] = input[field]!.DeepClone() };
                return input;
            case Mutation.Null:
                input[field] = null;
                return input;
            case Mutation.NonObject:
                return (pick % 3) switch
                {
                    0 => new JsonArray(input.DeepClone()),
                    1 => JsonValue.Create(input.ToJsonString()),
                    _ => JsonValue.Create(pick),
                };
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
    }

    // How many generated inputs one run sends through both engines, in one batch to the twin.
    private const int Inputs = 500;

    [Test]
    public void Generated_inputs_get_the_same_verdict_in_both_engines()
    {
        var ir = (JsonObject)JsonNode.Parse(IrExporter.Export(RangeMachine.Built))!;
        Gen.Select(AValidInput, AMutation, Gen.Int[0, 1_000])
            .Select(
                (input, mutation, pick) => (Mutation: mutation, Sent: Apply(mutation, input, pick))
            )
            .Array[Inputs]
            .Sample(
                batch =>
                {
                    var twin = TwinEngine.Replay(
                        new Dictionary<string, JsonObject> { ["range"] = ir },
                        batch
                            .Select(b => new TwinEngine.Sequence(
                                "range",
                                RangeMachine.Open(),
                                [
                                    new TwinEngine.Step(
                                        "advance",
                                        nameof(RangeTrigger.Build),
                                        b.Sent?.DeepClone()
                                    ),
                                ]
                            ))
                            .ToList()
                    );

                    for (var i = 0; i < batch.Length; i++)
                    {
                        var (mutation, sent) = batch[i];
                        var result = RangeMachine.Machine.Advance(
                            RangeMachine.Open(),
                            nameof(RangeTrigger.Build),
                            sent?.DeepClone()
                        );

                        if (mutation == Mutation.None)
                            result
                                .Should()
                                .BeOfType<AdvanceResult.Transitioned>(
                                    $"a valid input passes its schema ({sent?.ToJsonString()})"
                                );
                        else
                            result
                                .Should()
                                .BeOfType<AdvanceResult.Rejected>(
                                    $"{mutation} breaks the schema ({sent?.ToJsonString()})"
                                )
                                .Which.Reason.Should()
                                .Be(
                                    RefusalCode.InvalidInput,
                                    $"{mutation} breaks the input's schema, and nothing else does ({Adr})"
                                );

                        var (outcome, reason) = result switch
                        {
                            AdvanceResult.Transitioned => ("transitioned", (string?)null),
                            AdvanceResult.Rejected r => ("rejected", r.Reason.ToWire()),
                            _ => ("unknown", null),
                        };
                        var answer = twin[i][0];
                        (answer.Outcome, answer.Reason)
                            .Should()
                            .Be(
                                (outcome, reason),
                                $"the TypeScript twin gives {mutation} ({sent?.ToJsonString()}) the C# verdict ({Adr})"
                            );
                    }
                },
                iter: 1
            );
    }
}
