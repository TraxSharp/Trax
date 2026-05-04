using FluentAssertions;
using Trax.Mediator.Exceptions;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

[TestFixture]
public class MediatorExceptionsTests
{
    [Test]
    public void AmbiguousTrainNameException_ExposesRequestedNameAndCandidates()
    {
        var ex = new AmbiguousTrainNameException("MyTrain", new[] { "Foo.MyTrain", "Bar.MyTrain" });

        ex.RequestedName.Should().Be("MyTrain");
        ex.CandidateFullNames.Should().HaveCount(2).And.Contain("Foo.MyTrain");
        ex.Message.Should()
            .Contain("MyTrain")
            .And.Contain("Foo.MyTrain")
            .And.Contain("Bar.MyTrain");
    }

    [Test]
    public void TrainInputValidationException_ExposesNameAndSizes()
    {
        var ex = new TrainInputValidationException("Trax.X.MyTrain", 1024, 256);

        ex.TrainName.Should().Be("Trax.X.MyTrain");
        ex.ObservedBytes.Should().Be(1024);
        ex.MaxBytes.Should().Be(256);
        ex.Message.Should().Be("The train input failed validation.");
    }

    [Test]
    public void TrainNotFoundException_ExposesRequestedNameButNotInMessage()
    {
        var ex = new TrainNotFoundException("Trax.X.MissingTrain");

        ex.RequestedName.Should().Be("Trax.X.MissingTrain");
        // Message intentionally generic — must not leak the requested name.
        ex.Message.Should().NotContain("Trax.X.MissingTrain");
        ex.Message.Should().Be("The requested train was not found.");
    }
}
