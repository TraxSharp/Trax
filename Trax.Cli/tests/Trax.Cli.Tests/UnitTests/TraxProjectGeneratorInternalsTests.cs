using FluentAssertions;
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
    public void PatchProgramCs_NoProgramFile_NoOp()
    {
        var hubDir = Path.Combine(_tempDir, "EmptyHub");
        Directory.CreateDirectory(hubDir);

        Action act = () => TraxProjectGenerator.PatchProgramCs(hubDir, "MyApi");

        act.Should().NotThrow();
        Directory.GetFiles(hubDir).Should().BeEmpty();
    }

    [Test]
    public void PatchProgramCs_RewritesAssemblyScanAndAddsUsing()
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
        updated.Should().Contain("using MyApi.Trains;");
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
}
