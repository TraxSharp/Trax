namespace Trax.Samples.Tests.Meta.Tests;

/// <summary>
/// Every sample that broadcasts over RabbitMQ names an exchange of its own. Each process bound to
/// an exchange receives every event published on it, and the samples share one broker in
/// docker-compose and in CI, so two samples on the default <c>trax.lifecycle</c> deliver each
/// other's runs to their subscribers. The processes of one sample share its exchange, so the unit
/// is the sample folder: every <c>UseRabbitMq(</c> call sets <c>ExchangeName</c>, the folder
/// declares exactly one name, and no other folder declares it.
///
/// <para>Not ADR-enforcing: it keeps the samples from hearing each other on a shared broker, which
/// is how the broker works rather than a choice between alternatives.</para>
/// </summary>
[TestFixture]
public class RabbitMqExchangePerSampleTests
{
    private const string DefaultExchange = "trax.lifecycle";

    private static readonly Regex UseRabbitMqCall = new(@"\bUseRabbitMq\(", RegexOptions.Compiled);

    private static readonly Regex DeclaredName = new(
        @"ExchangeName\s*=\s*""(?<name>[^""]+)""",
        RegexOptions.Compiled
    );

    [Test]
    public void Every_rabbitmq_sample_names_an_exchange_no_other_sample_uses()
    {
        var samples = Path.Combine(RepoRoot.Path, "samples");
        var offenders = new List<string>();
        var foldersByName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var callers = 0;

        foreach (var folder in Directory.EnumerateDirectories(samples).OrderBy(f => f))
        {
            var sample = Path.GetFileName(folder);
            var files = SourceFiles
                .CSharp(Path.Combine("samples", sample))
                .Select(f => (File: f, Text: File.ReadAllText(f)))
                .Select(f => (f.File, f.Text, Code: SourceText.StripCommentsAndStrings(f.Text)))
                .ToList();

            var calls = files
                .SelectMany(f =>
                    UseRabbitMqCall
                        .Matches(f.Code)
                        .Select(m => (f.File, Call: CallText(f.Code, m.Index)))
                )
                .ToList();
            if (calls.Count == 0)
                continue;
            callers += calls.Count;

            foreach (var (file, call) in calls)
                if (!call.Contains("ExchangeName", StringComparison.Ordinal))
                    offenders.Add(
                        $"{RepoRoot.Relative(file)}: UseRabbitMq without an ExchangeName uses "
                            + $"the shared default {DefaultExchange}"
                    );

            var names = files
                .SelectMany(f => DeclaredName.Matches(f.Text).Select(m => m.Groups["name"].Value))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (names.Count != 1)
                offenders.Add(
                    $"samples/{sample}: declares {names.Count} exchange names "
                        + $"({string.Join(", ", names)}); its processes must share exactly one"
                );

            foreach (var name in names)
            {
                if (name == DefaultExchange)
                    offenders.Add($"samples/{sample}: uses the shared default {DefaultExchange}");
                if (!foldersByName.TryGetValue(name, out var folders))
                    foldersByName[name] = folders = [];
                folders.Add(sample);
            }
        }

        foreach (var (name, folders) in foldersByName.Where(kv => kv.Value.Count > 1))
            offenders.Add($"{name} is used by {string.Join(", ", folders)}");

        callers.Should().BeGreaterThan(0, "the EnergyHub and ContentShield samples use RabbitMQ");
        offenders
            .Should()
            .BeEmpty(
                "samples on one broker and one exchange receive each other's train events. "
                    + "Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    /// <summary>The text of the call starting at <paramref name="start"/>, to its closing parenthesis.</summary>
    private static string CallText(string source, int start)
    {
        var open = source.IndexOf('(', start);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '(')
                depth++;
            else if (source[i] == ')' && --depth == 0)
                return source[start..(i + 1)];
        }
        return source[start..];
    }
}
