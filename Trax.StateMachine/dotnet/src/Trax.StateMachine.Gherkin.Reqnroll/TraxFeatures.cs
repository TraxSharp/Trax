using System.Text.RegularExpressions;

namespace Trax.StateMachine.Gherkin.Reqnroll;

/// <summary>
/// Configures the bindings for a test run. Call <see cref="Configure"/> once, from a <c>[BeforeTestRun]</c> hook, after
/// the host under test has started.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/feature-bindings">SDK reference</seealso>
public static class TraxFeatures
{
    private static TraxFeatureDriverOptions? s_options;

    /// <summary>Sets what the bindings run against.</summary>
    /// <exception cref="ArgumentException">A required option is not set.</exception>
    public static void Configure(Action<TraxFeatureDriverOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new TraxFeatureDriverOptions();
        configure(options);
        if (!options.HasRequired)
            throw new ArgumentException(
                "TraxFeatures.Configure needs Host, Client, Glossary and Credential set.",
                nameof(configure)
            );
        s_options = options;
    }

    internal static TraxFeatureDriverOptions Options =>
        s_options
        ?? throw new InvalidOperationException(
            "The Trax feature bindings are not configured: call TraxFeatures.Configure(...) from a [BeforeTestRun] hook."
        );
}
