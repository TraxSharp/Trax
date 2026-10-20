using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;
using Trax.Effect.StateMachine.Tests.Helpers;

namespace Trax.Effect.StateMachine.Tests.Differential;

/// <summary>
/// Every value of <see cref="StrictInputMachine.Cases"/> sent through the C# engine and through the TypeScript twin
/// built from the exported IR: both give one verdict and one refusal code, and that verdict is the CLR's, from reading
/// the input into the record through the typed surface's JSON and writing it back (an enum's names, a Guid's, date's,
/// time's, duration's, URI's and char's format, a number's range and a dictionary's keys included). A case the engines
/// refuse though the CLR reads it says why, and is checked to be one the CLR does read.
/// </summary>
[Property("adr", Adr)]
public class StrictInputParityTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    [Test]
    public void Each_value_gets_one_verdict_in_both_engines()
    {
        var cases = StrictInputMachine.Cases;
        var ir = (JsonObject)JsonNode.Parse(IrExporter.Export(StrictInputMachine.Built))!;
        var start = StrictInputMachine.Open();
        var inputs = cases
            .Select(c => (JsonNode?)StrictInputMachine.InputWith(c.Field, c.Value?.DeepClone()))
            .Prepend(StrictInputMachine.ValidInput())
            .ToList();

        var twin = TwinEngine.Replay(
            new Dictionary<string, JsonObject> { ["strict"] = ir },
            inputs
                .Select(input => new TwinEngine.Sequence(
                    "strict",
                    start,
                    [new TwinEngine.Step("advance", nameof(StrictTrigger.Submit), input)]
                ))
                .ToList()
        );

        var names = cases.Select(c => c.Name).Prepend("valid input").ToList();
        var expected = cases.Select(c => c.Valid).Prepend(true).ToList();
        var diffs = new List<string>();
        if (!StrictInputMachine.ClrReads(StrictInputMachine.ValidInput(), out var invalid))
            diffs.Add($"valid input: the CLR does not read it: {invalid}");
        foreach (var c in cases.Where(c => c.Stricter is not null))
            if (!TypedSurfaceJson.Reads<StrictInputMachine.StrictInput>(c.Input(), out _, out _))
                diffs.Add($"{c.Name}: marked stricter than the CLR, which does not read it either");
        for (var i = 0; i < inputs.Count; i++)
        {
            var result = StrictInputMachine.Machine.Advance(
                start,
                nameof(StrictTrigger.Submit),
                inputs[i]?.DeepClone()
            );
            var (outcome, reason) = result switch
            {
                AdvanceResult.Transitioned => ("transitioned", (string?)null),
                AdvanceResult.Rejected r => ("rejected", r.Reason.ToWire()),
                _ => ("unknown", null),
            };
            var answer = twin[i][0];
            if (answer.Outcome != outcome || answer.Reason != reason)
                diffs.Add(
                    $"{names[i]}: C# {outcome} {reason}, twin {answer.Outcome} {answer.Reason}"
                );
            var want = expected[i] ? "transitioned" : "rejected";
            if (outcome != want)
                diffs.Add($"{names[i]}: C# {outcome} {reason}, the CLR type says {want}");
            if (!expected[i] && reason != RefusalCode.InvalidInput.ToWire())
                diffs.Add($"{names[i]}: refused as {reason}, not invalid-input");
        }

        diffs
            .Should()
            .BeEmpty(
                $"both engines give each value the verdict of its CLR type ({Adr}):\n"
                    + string.Join("\n", diffs)
            );
    }
}
