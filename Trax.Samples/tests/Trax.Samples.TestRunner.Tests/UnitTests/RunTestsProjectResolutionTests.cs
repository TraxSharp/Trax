using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Trax.Samples.TestRunner.Services;
using Trax.Samples.TestRunner.Trains.RunTests;
using Trax.Samples.TestRunner.Trains.RunTests.Junctions;

namespace Trax.Samples.TestRunner.Tests.UnitTests;

/// <summary>
/// A caller names a test project; it never supplies a path. The name is resolved against the
/// projects <see cref="TestProjectRegistry"/> discovered under the configured root, so nothing
/// outside that list is built or loaded, and the resolved path reaches <c>dotnet build</c> as a
/// single argument whatever characters it holds.
/// </summary>
[TestFixture]
public class RunTestsProjectResolutionTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "RunTestsResolution_" + Guid.NewGuid());
        var projectDir = Path.Combine(_root, "tests", "Sample.Tests");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(
            Path.Combine(projectDir, "Sample.Tests.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><PackageReference Include="NUnit" Version="4.*" /></ItemGroup>
            </Project>
            """
        );
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [Test]
    public async Task An_unregistered_project_name_is_refused()
    {
        var junction = new ResolveProjectJunction(Registry());

        Func<Task> act = () =>
            junction.Run(new RunTestsInput { ProjectName = "/etc/Evil.csproj", Build = true });

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*not a registered test project*");
    }

    [Test]
    public async Task A_registered_name_resolves_to_the_path_the_registry_found()
    {
        var junction = new ResolveProjectJunction(Registry());

        var run = await junction.Run(
            new RunTestsInput { ProjectName = "Sample.Tests", Build = false }
        );

        run.Project.ProjectPath.Should()
            .Be(
                Path.Combine(
                    Path.GetFullPath(_root),
                    "tests",
                    "Sample.Tests",
                    "Sample.Tests.csproj"
                )
            );
        run.Build.Should().BeFalse();
    }

    [Test]
    public void A_path_containing_a_quote_reaches_dotnet_build_as_one_argument()
    {
        const string path = "/tmp/a\" -p:Evil=1 \"/Sample.Tests.csproj";

        var info = BuildProjectJunction.BuildStartInfo(path);

        info.Arguments.Should().BeEmpty();
        info.ArgumentList.Should().Equal("build", path, "--no-restore", "-c", "Debug");
    }

    private TestProjectRegistry Registry() =>
        new(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?> { ["TestRunner:Root"] = _root }
                )
                .Build(),
            NullLogger<TestProjectRegistry>.Instance
        );
}
