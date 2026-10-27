using AwesomeAssertions;
using Trax.Cli.Generator;

namespace Trax.Cli.Tests.UnitTests;

[TestFixture]
public class TraxProjectGeneratorInternalsTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"trax-cli-gen-int-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public void IsDotnetAvailable_OnDevHost_ReturnsTrue()
    {
        // The build environment must have dotnet for these tests to run at all.
        TraxProjectGenerator.IsDotnetAvailable().Should().BeTrue();
    }

    [Test]
    public void Dotnet_is_looked_up_on_PATH_by_name_never_run_bare()
    {
        var asked = new List<string>();

        var found = TraxProjectGenerator.Dotnet(
            _ => null,
            name =>
            {
                asked.Add(name);
                return "/usr/share/dotnet/dotnet";
            }
        );

        found.Should().Be("/usr/share/dotnet/dotnet");
        asked.Should().Equal("dotnet");
    }

    [Test]
    public void Dotnet_is_DOTNET_HOST_PATH_when_it_is_an_absolute_file()
    {
        var host = Path.Combine(_tempDir, "dotnet");
        File.WriteAllText(host, "");

        TraxProjectGenerator
            .Dotnet(n => n == "DOTNET_HOST_PATH" ? host : null, _ => "/elsewhere/dotnet")
            .Should()
            .Be(host);
    }

    [TestCase("dotnet")]
    [TestCase("./dotnet")]
    [TestCase("bin/dotnet")]
    public void A_relative_DOTNET_HOST_PATH_is_not_run(string relative) =>
        TraxProjectGenerator
            .Dotnet(n => n == "DOTNET_HOST_PATH" ? relative : null, _ => null, _ => true)
            .Should()
            .BeNull();

    [Test]
    public void A_DOTNET_HOST_PATH_that_is_not_a_file_falls_back_to_PATH() =>
        TraxProjectGenerator
            .Dotnet(
                n => n == "DOTNET_HOST_PATH" ? Path.Combine(_tempDir, "missing") : null,
                _ => "/usr/bin/dotnet"
            )
            .Should()
            .Be("/usr/bin/dotnet");

    [Test]
    public void AddProjectReference_AppendsItemGroupBeforeProjectClose()
    {
        var apiDir = Path.Combine(_tempDir, "MyApi.Hub");
        Directory.CreateDirectory(apiDir);
        var csprojPath = Path.Combine(apiDir, "MyApi.Hub.csproj");
        File.WriteAllText(
            csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """
        );

        TraxProjectGenerator.AddProjectReference(apiDir, "MyApi.Hub", "MyApi.Trains");

        var updated = File.ReadAllText(csprojPath);
        updated.Should().Contain("<ProjectReference");
        updated.Should().Contain(@"..\MyApi.Trains\MyApi.Trains.csproj");
        updated.TrimEnd().Should().EndWith("</Project>");
    }

    [Test]
    public void PatchProgramCs_RewritesAssemblyScanWithoutAUsing()
    {
        var hubDir = Path.Combine(_tempDir, "Hub");
        Directory.CreateDirectory(hubDir);
        var programPath = Path.Combine(hubDir, "Program.cs");
        File.WriteAllText(
            programPath,
            """
            using Microsoft.Extensions.DependencyInjection;
            using Trax.Mediator.Extensions;

            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddTrax(t => t.AddMediator(typeof(Program).Assembly));
            var app = builder.Build();
            app.Run();
            """
        );

        TraxProjectGenerator.PatchProgramCs(hubDir, "MyApi");

        var updated = File.ReadAllText(programPath);
        updated
            .Should()
            .Contain("typeof(Program).Assembly, typeof(MyApi.Trains.ManifestNames).Assembly");
        updated.Should().NotContain("using MyApi.Trains;");
    }

    [Test]
    public void PatchProgramCs_TrainsUsingAlreadyPresent_NotDuplicated()
    {
        var hubDir = Path.Combine(_tempDir, "Hub2");
        Directory.CreateDirectory(hubDir);
        var programPath = Path.Combine(hubDir, "Program.cs");
        File.WriteAllText(
            programPath,
            """
            using MyApi.Trains;
            using Trax.Mediator.Extensions;

            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddTrax(t => t.AddMediator(typeof(Program).Assembly));
            """
        );

        TraxProjectGenerator.PatchProgramCs(hubDir, "MyApi");

        var updated = File.ReadAllText(programPath);
        var occurrences = System
            .Text.RegularExpressions.Regex.Matches(updated, "using MyApi.Trains;")
            .Count;
        occurrences.Should().Be(1);
    }

    [Test]
    public void Generate_OutputExistsWithoutForce_Throws()
    {
        var outputDir = Path.Combine(_tempDir, "exists");
        Directory.CreateDirectory(outputDir);
        var generator = new TraxProjectGenerator();

        Action act = () =>
            generator.Generate(
                new Trax.Cli.Models.ApiSchema { SourceFile = "x", SchemaType = "openapi" },
                outputDir,
                "Proj",
                force: false
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*--force*");
    }

    [Test]
    public void DotnetNewFailure_WhenTheTemplateIsMissing_NamesThePackageThatShipsIt()
    {
        var message = TraxProjectGenerator.DotnetNewFailure(
            103,
            "No templates or subcommands found matching: 'trax-hub'."
        );

        message
            .Should()
            .Contain(
                "dotnet new install Trax.Samples.Templates",
                "Trax.Samples is not a package on nuget.org; the templates ship in Trax.Samples.Templates"
            );
    }
}
