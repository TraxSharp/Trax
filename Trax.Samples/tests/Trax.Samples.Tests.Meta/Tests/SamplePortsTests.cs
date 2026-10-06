namespace Trax.Samples.Tests.Meta.Tests;

/// <summary>
/// Every port a sample listens on is in the samples' ranges, 5200-5299 and 5310-5319, and
/// belongs to one sample. A sample on a port outside them collides with whatever else a
/// developer runs (5000 is the framework default), and
/// two samples on one port cannot run side by side. The ports are read from each launch profile's
/// <c>applicationUrl</c> and from every <c>http://localhost:N</c> and <c>port = N</c> in a
/// sample's C#.
///
/// <para>Not ADR-enforcing: it keeps a numbering convention, not a choice between
/// alternatives.</para>
/// </summary>
[TestFixture]
public class SamplePortsTests
{
    /// <summary>
    /// Vite's dev server, where the React clients run; the hosts name it as an allowed origin.
    /// It is the browser's port, not a sample host's.
    /// </summary>
    private const int ViteDevServer = 5173;

    private static readonly Regex LocalUrl = new(
        @"https?://(localhost|127\.0\.0\.1):(?<port>\d+)",
        RegexOptions.Compiled
    );

    /// <summary>
    /// A port held in a variable and spliced into the URL later, as in <c>var port = 5099;</c>.
    /// Lower case only, so a connection string's <c>Port=5432</c> is not read as one.
    /// </summary>
    private static readonly Regex PortVariable = new(
        @"\bport\s*=\s*(?<port>\d{4,5})\b",
        RegexOptions.Compiled
    );

    private static bool InRange(int port) => port is (>= 5200 and <= 5299) or (>= 5310 and <= 5319);

    [Test]
    public void Every_sample_port_is_in_range_and_owned_by_one_sample()
    {
        var samples = Path.Combine(RepoRoot.Path, "samples");
        var files = Directory
            .EnumerateFiles(samples, "launchSettings.json", SearchOption.AllDirectories)
            .Concat(SourceFiles.CSharp("samples"))
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            );

        var offenders = new List<string>();
        var foldersByPort = new Dictionary<int, SortedSet<string>>();
        foreach (var file in files)
        {
            var folder = Path.GetRelativePath(samples, file).Split(Path.DirectorySeparatorChar)[0];
            var text = File.ReadAllText(file);
            foreach (Match match in LocalUrl.Matches(text).Concat(PortVariable.Matches(text)))
            {
                var port = int.Parse(match.Groups["port"].Value);
                if (port == ViteDevServer)
                    continue;
                if (!InRange(port))
                    offenders.Add($"{RepoRoot.Relative(file)}: port {port} is outside the ranges");
                if (!foldersByPort.TryGetValue(port, out var folders))
                    foldersByPort[port] = folders = [];
                folders.Add(folder);
            }
        }

        foreach (var (port, folders) in foldersByPort.Where(kv => kv.Value.Count > 1))
            offenders.Add($"port {port} is used by {string.Join(", ", folders)}");

        foldersByPort.Should().NotBeEmpty("the samples listen on ports");
        offenders
            .Should()
            .BeEmpty(
                "every sample listens on a port of its own in 5200-5299 or 5310-5319. Offenders:\n  "
                    + string.Join("\n  ", offenders.Distinct())
            );
    }
}
