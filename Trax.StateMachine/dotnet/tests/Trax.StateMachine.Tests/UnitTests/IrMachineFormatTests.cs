using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// How <see cref="IrMachine"/> reads an IR's <c>irFormat</c> where the kit cannot say it: the runner writes JSON from
/// node, which writes <c>1.0</c> as <c>1</c>. The TypeScript and Rust engines read a format by its number, so
/// <c>1.0</c> is format 1 and <c>1.5</c> is no format.
/// </summary>
public class IrMachineFormatTests
{
    private static JsonObject Turnstile(string irFormat)
    {
        var ir = JsonNode.Parse(File.ReadAllText(FixturePaths.IrFile("turnstile")))!.AsObject();
        ir["irFormat"] = JsonNode.Parse(irFormat);
        return JsonNode.Parse(ir.ToJsonString())!.AsObject();
    }

    [Test]
    public void A_format_written_with_a_fraction_of_zero_is_that_format()
    {
        var build = () => new IrMachine(Turnstile("1.0"));

        build.Should().NotThrow();
    }

    [Test]
    public void A_format_with_a_fraction_is_no_format()
    {
        var build = () => new IrMachine(Turnstile("1.5"));

        build.Should().Throw<ArgumentException>();
    }
}
