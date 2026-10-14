using AwesomeAssertions;
using Trax.Cli.Commands;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// How <c>trax</c> reads its command line. An npm package specifier starts with <c>@</c>, which
/// System.CommandLine would otherwise read as the name of a response file to expand.
/// </summary>
public class TraxCommandLineTests
{
    [Test]
    public void A_specifier_starting_with_an_at_sign_is_passed_through_as_written()
    {
        var result = TraxCommandLine.Parse([
            "machine",
            "generate",
            "--assembly",
            "machines.dll",
            "--import-style",
            "specifier",
            "--specifier",
            "@traxsharp/state-machine",
        ]);

        result.Errors.Select(e => e.Message).Should().BeEmpty();
        result.GetValue<string?>("--specifier").Should().Be("@traxsharp/state-machine");
    }
}
