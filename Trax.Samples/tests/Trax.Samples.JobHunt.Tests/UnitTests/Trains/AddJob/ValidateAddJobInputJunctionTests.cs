using FluentAssertions;
using NUnit.Framework;
using Trax.Samples.JobHunt.Trains.AddJob;
using Trax.Samples.JobHunt.Trains.AddJob.Junctions;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Trains.AddJob;

[TestFixture]
public class ValidateAddJobInputJunctionTests
{
    private readonly ValidateAddJobInputJunction _junction = new();

    [Test]
    public async Task Run_UrlOnly_ReturnsInput()
    {
        var input = new AddJobInput { UserId = "alice", Url = "https://example.com/job" };

        var result = await _junction.Run(input);

        result.Should().BeSameAs(input);
    }

    [Test]
    public async Task Run_PastedFieldsComplete_ReturnsInput()
    {
        var input = new AddJobInput
        {
            UserId = "alice",
            PastedTitle = "Senior Engineer",
            PastedCompany = "Acme",
            PastedDescription = "Build things",
        };

        var result = await _junction.Run(input);

        result.Should().BeSameAs(input);
    }

    [Test]
    public void Run_NoUrlNoPaste_Throws()
    {
        var input = new AddJobInput { UserId = "alice" };

        var act = () => _junction.Run(input);

        act.Should().ThrowAsync<ArgumentException>().WithMessage("*URL*pasted*");
    }

    [Test]
    public void Run_PartialPasteMissingTitle_Throws()
    {
        var input = new AddJobInput
        {
            UserId = "alice",
            PastedCompany = "Acme",
            PastedDescription = "Build things",
        };

        var act = () => _junction.Run(input);

        act.Should().ThrowAsync<ArgumentException>();
    }

    [Test]
    public void Run_PartialPasteMissingCompany_Throws()
    {
        var input = new AddJobInput
        {
            UserId = "alice",
            PastedTitle = "Engineer",
            PastedDescription = "Build things",
        };

        var act = () => _junction.Run(input);

        act.Should().ThrowAsync<ArgumentException>();
    }

    [Test]
    public void Run_BothUrlAndPaste_Throws()
    {
        var input = new AddJobInput
        {
            UserId = "alice",
            Url = "https://example.com/job",
            PastedTitle = "Engineer",
            PastedCompany = "Acme",
            PastedDescription = "Build things",
        };

        var act = () => _junction.Run(input);

        act.Should().ThrowAsync<ArgumentException>().WithMessage("*not both*");
    }

    [Test]
    public void Run_EmptyUserId_Throws()
    {
        var input = new AddJobInput
        {
            UserId = "",
            PastedTitle = "Engineer",
            PastedCompany = "Acme",
            PastedDescription = "Build things",
        };

        var act = () => _junction.Run(input);

        act.Should().ThrowAsync<ArgumentException>().WithMessage("*UserId*");
    }

    [Test]
    public void Run_WhitespaceUserId_Throws()
    {
        var input = new AddJobInput
        {
            UserId = "   ",
            PastedTitle = "Engineer",
            PastedCompany = "Acme",
            PastedDescription = "Build things",
        };

        var act = () => _junction.Run(input);

        act.Should().ThrowAsync<ArgumentException>().WithMessage("*UserId*");
    }
}
