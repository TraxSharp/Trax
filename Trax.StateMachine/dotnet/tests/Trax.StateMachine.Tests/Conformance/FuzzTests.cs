using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.Conformance;

/// <summary>
/// The C# engine against the TypeScript one through the differential fuzzer (<c>conformance/fuzz.mjs</c>): the same
/// generated requests go to both, and the run fails on the first answer that differs, shrunk. A short run with a
/// fixed seed, so it is the same run every time; <c>TRAX_FUZZ_SEED</c> and <c>TRAX_FUZZ_CASES</c> make a long one.
/// </summary>
[Category(Categories.CrossEngine)]
public class FuzzTests
{
    private const string TypeScriptTestee = "--testee node ts/tools/conformance/testee-ts.mjs";

    [Test]
    public void The_compiled_CSharp_machines_answer_as_the_TypeScript_engine_does() =>
        Fuzz(new ConformanceTestee(), irs: false);

    [Test]
    public void The_CSharp_engine_built_from_random_IRs_answers_as_the_TypeScript_engine_does() =>
        Fuzz(new ConformanceTestee(fromIr: true), irs: true);

    private static void Fuzz(ConformanceTestee testee, bool irs)
    {
        var seed = Environment.GetEnvironmentVariable("TRAX_FUZZ_SEED") ?? "1";
        var cases = int.Parse(Environment.GetEnvironmentVariable("TRAX_FUZZ_CASES") ?? "300");
        var (exitCode, report) = TwinEngine.RunFuzzer(
            $"--seed {seed} --cases {cases}{(irs ? " --irs" : "")} {TypeScriptTestee}",
            line => Answer(testee, line),
            TimeSpan.FromMinutes(2) + TimeSpan.FromMilliseconds(cases * 5.0)
        );

        exitCode.Should().Be(0, report);
        report.Should().Contain("every testee answered alike");
    }

    // An engine that throws breaks the rule that every operation is total: answered as an error, which differs from
    // whatever the other engine answers, so the fuzzer reports and shrinks it.
    private static string Answer(ConformanceTestee testee, string line)
    {
        try
        {
            return testee.Answer(line);
        }
        catch (Exception ex)
        {
            return new JsonObject
            {
                ["id"] = JsonNode.Parse(line)?["id"]?.DeepClone(),
                ["error"] = $"threw {ex.GetType().Name}: {ex.Message}",
            }.ToJsonString();
        }
    }
}
