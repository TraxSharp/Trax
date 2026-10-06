using System.Net;
using System.Net.Sockets;
using AwesomeAssertions;
using Trax.Samples.Templates.Tests.Utils;
using static Trax.Samples.Templates.Tests.Utils.Dotnet;

namespace Trax.Samples.Templates.Tests.IntegrationTests;

/// <summary>
/// What `dotnet new` hands a reader: each template is packed, installed into a private template
/// hive and scaffolded into a directory outside this repository, then built with warnings as
/// errors, its own test project run, and the built application started in Development (through
/// its launch profile, as `dotnet run` does) and in Production.
///
/// <para>
/// The package pins every Trax package at the version it was packed with, and
/// <see cref="ScaffoldFeed"/> packs those packages from this commit at that version, so a
/// scaffold builds and runs against the code under test.
/// </para>
///
/// <para>Enforces <c>docs/adr/0003-templates-serve-the-dashboard-only-in-development.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0003-templates-serve-the-dashboard-only-in-development.md")]
public class ScaffoldedTemplateTests
{
    private const string Adr =
        "see docs/adr/0003-templates-serve-the-dashboard-only-in-development.md";

    private string _workDir = null!;
    private string _hive = null!;
    private readonly Dictionary<string, Task<string>> _scaffolds = new();

    [OneTimeSetUp]
    public async Task PackAndInstallTheTemplates()
    {
        // A directory with no Directory.Packages.props or Directory.Build.props above it, which
        // is where a consumer runs `dotnet new`. Its nuget.config stands in for the packages
        // being published.
        _workDir = Path.Combine(Path.GetTempPath(), "trax-template-scaffold-" + Guid.NewGuid());
        _hive = Path.Combine(_workDir, "hive");
        Directory.CreateDirectory(_workDir);

        var packed = await ScaffoldFeed.Get();
        ScaffoldFeed.WriteNuGetConfig(_workDir, packed);
        await Run(_workDir, "new", "install", packed.TemplatesNupkg, "--debug:custom-hive", _hive);
    }

    [OneTimeTearDown]
    public void DeleteTheWorkDirectory()
    {
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, recursive: true);
    }

    [TestCase("trax-hub")]
    [TestCase("trax-scheduler")]
    [TestCase("trax-api")]
    public async Task Scaffold_Build_HasNoWarnings(string shortName)
    {
        var dir = await Scaffold(shortName);

        var build = await Run(dir, allowFailure: true, "build", "-warnaserror", "--no-restore");

        build.ExitCode.Should().Be(0, $"a scaffolded {shortName} builds clean:\n{build.Output}");
    }

    [TestCase("trax-hub")]
    [TestCase("trax-scheduler")]
    [TestCase("trax-api")]
    public async Task Scaffold_TestProject_Passes(string shortName)
    {
        var dir = await Scaffold(shortName);

        var test = await Run(dir, allowFailure: true, "test", Path.Combine("tests", "App.Tests"));

        test.ExitCode.Should()
            .Be(0, $"the test project a scaffolded {shortName} ships passes:\n{test.Output}");
        test.Output.Should().Contain("Passed!", "the test project has tests to run");
    }

    [TestCase("trax-hub")]
    [TestCase("trax-scheduler")]
    [TestCase("trax-api")]
    public async Task Scaffold_Readme_LinksToTheDocs(string shortName)
    {
        var dir = await Scaffold(shortName);

        var readme = await File.ReadAllTextAsync(Path.Combine(dir, "README.md"));

        readme.Should().Contain("https://traxsharp.net/docs/reference/templates");
        readme
            .Should()
            .Contain("dotnet test tests/App.Tests", "the README says how to run the tests");
    }

    [TestCase("trax-hub", "/trax", HttpStatusCode.OK)]
    [TestCase("trax-scheduler", "/trax", HttpStatusCode.OK)]
    [TestCase("trax-api", "/trax/health", HttpStatusCode.OK)]
    public async Task Scaffold_DotnetRun_StartsInDevelopmentWithoutAnAddressOverride(
        string shortName,
        string path,
        HttpStatusCode expected
    )
    {
        var dir = await Scaffold(shortName);
        var url = $"http://127.0.0.1:{FreePort()}";

        // The launch profile sets Development; --urls only moves the port off the fixed one.
        await using var app = await Start(
            dir,
            new Dictionary<string, string>(),
            "Application started",
            "run",
            "--no-build",
            "--",
            "--urls",
            url
        );

        using var http = new HttpClient();
        (await http.GetAsync(url + path))
            .StatusCode.Should()
            .Be(expected, $"dotnet run starts in Development and serves {path} ({Adr})");
        app.Output.Should().Contain("Hosting environment: Development");
        app.Output.Should()
            .NotContain(
                "Overriding address",
                "the URL has one source; appsettings.json carrying a Kestrel endpoint as well "
                    + "warns on every start"
            );
    }

    [TestCase("trax-hub", "/trax", HttpStatusCode.NotFound)]
    [TestCase("trax-hub", "/trax/health", HttpStatusCode.OK)]
    [TestCase("trax-scheduler", "/trax", HttpStatusCode.NotFound)]
    [TestCase("trax-api", "/trax/health", HttpStatusCode.OK)]
    public async Task Scaffold_StartsInProduction_WithoutTheDevelopmentOnlyParts(
        string shortName,
        string path,
        HttpStatusCode expected
    )
    {
        var dir = await Scaffold(shortName);
        var url = $"http://127.0.0.1:{FreePort()}";

        await using var app = await Start(
            dir,
            new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
                ["ASPNETCORE_URLS"] = url,
            },
            "Application started",
            "run",
            "--no-build",
            "--no-launch-profile"
        );

        using var http = new HttpClient();
        (await http.GetAsync(url + path))
            .StatusCode.Should()
            .Be(
                expected,
                $"a scaffold started outside Development serves no dashboard and still starts ({Adr})"
            );
        app.Output.Should().Contain("Hosting environment: Production");
    }

    /// <summary>
    /// Scaffolds <paramref name="shortName"/> as a project named <c>App</c>, restores and builds
    /// it, once per fixture, and returns its directory.
    /// </summary>
    private Task<string> Scaffold(string shortName)
    {
        lock (_scaffolds)
        {
            if (!_scaffolds.TryGetValue(shortName, out var scaffold))
                _scaffolds[shortName] = scaffold = ScaffoldOnce(shortName);
            return scaffold;
        }
    }

    private async Task<string> ScaffoldOnce(string shortName)
    {
        var dir = Path.Combine(_workDir, shortName, "App");
        await Run(
            _workDir,
            "new",
            shortName,
            "--name",
            "App",
            "--output",
            dir,
            "--debug:custom-hive",
            _hive
        );
        await Run(dir, "build");
        return dir;
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
