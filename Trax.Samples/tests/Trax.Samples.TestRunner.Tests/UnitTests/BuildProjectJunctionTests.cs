using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Trax.Samples.TestRunner.Trains.RunTests;
using Trax.Samples.TestRunner.Trains.RunTests.Junctions;

namespace Trax.Samples.TestRunner.Tests.UnitTests;

[TestFixture]
public class ExecuteTestsJunctionFileNotFoundTests
{
    [Test]
    public async Task Run_DllMissing_ThrowsFileNotFoundException()
    {
        var junction = new ExecuteTestsJunction(NullLogger<ExecuteTestsJunction>.Instance);
        var input = new RunTestsInput
        {
            ProjectName = "NoBuild",
            ProjectPath = "/tmp/__missing__/Foo.csproj",
            Build = false,
        };

        Func<Task> act = () => junction.Run(input);

        await act.Should()
            .ThrowAsync<FileNotFoundException>()
            .WithMessage("*Test assembly not found*");
    }
}

[TestFixture]
public class BuildProjectJunctionTests
{
    [Test]
    public async Task Run_BuildFalse_SkipsAndReturnsInputUnchanged()
    {
        var junction = new BuildProjectJunction(NullLogger<BuildProjectJunction>.Instance);
        var input = new RunTestsInput
        {
            ProjectName = "Some.Project",
            ProjectPath = "/does/not/exist/Some.Project.csproj",
            Build = false,
        };

        var result = await junction.Run(input);

        result.Should().BeSameAs(input);
        result.ProjectName.Should().Be("Some.Project");
    }

    [Test]
    public async Task Run_BuildTrueOnInvalidPath_Throws()
    {
        var junction = new BuildProjectJunction(NullLogger<BuildProjectJunction>.Instance);
        var input = new RunTestsInput
        {
            ProjectName = "NoSuchProject",
            ProjectPath = "/tmp/__definitely_does_not_exist__.csproj",
            Build = true,
        };

        Func<Task> act = () => junction.Run(input);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Build failed*");
    }
}
