using AwesomeAssertions;
using Trax.Core.Exceptions;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Tests.UnitTests;

/// <summary>
/// Which type a remote run's output is read into on the calling side: the expected type when it
/// is concrete, whatever the response names, and otherwise only an already-loaded implementation
/// of it that the response names. Enforces
/// <c>docs/adr/0006-a-runner-requires-an-authorization-posture.md</c>.
/// </summary>
[Property("adr", "docs/adr/0006-a-runner-requires-an-authorization-posture.md")]
[TestFixture]
public class RemoteRunOutputTests
{
    private const string Adr = "docs/adr/0006-a-runner-requires-an-authorization-posture.md";

    public interface IShape
    {
        string Name { get; }
    }

    public abstract record ShapeBase : IShape
    {
        public string Name { get; init; } = "";
    }

    public record Circle : ShapeBase
    {
        public double Radius { get; init; }
    }

    public record Unrelated
    {
        public string Name { get; init; } = "";
    }

    [Test]
    public void ConcreteExpectedType_IsReadIntoItself_WhateverTheResponseNames() =>
        RemoteRunOutput
            .ReadAs(typeof(Unrelated), typeof(Circle).FullName)
            .Should()
            .Be(typeof(Unrelated), $"a concrete expected type is never swapped (see {Adr})");

    [TestCase(typeof(IShape))]
    [TestCase(typeof(ShapeBase))]
    public void InterfaceOrAbstract_ReadsTheLoadedImplementationTheResponseNames(Type expected)
    {
        RemoteRunOutput.ReadAs(expected, typeof(Circle).FullName).Should().Be(typeof(Circle));
        RemoteRunOutput
            .ReadAs(expected, typeof(Circle).AssemblyQualifiedName)
            .Should()
            .Be(typeof(Circle));

        var output = RemoteRunOutput.Read(
            """{"name":"c","radius":2}""",
            expected,
            typeof(Circle).FullName
        );
        output.Should().BeOfType<Circle>().Which.Radius.Should().Be(2);
    }

    [TestCase(typeof(IShape))]
    [TestCase(typeof(ShapeBase))]
    public void ALoadedTypeThatDoesNotImplementTheExpectedType_IsRefused(Type expected) =>
        FluentActions
            .Invoking(() => RemoteRunOutput.ReadAs(expected, typeof(Unrelated).FullName))
            .Should()
            .Throw<TrainException>(
                $"only an implementation of the expected type is read (see {Adr})"
            );

    [Test]
    public void TheExpectedTypeItself_IsRefused() =>
        FluentActions
            .Invoking(() => RemoteRunOutput.ReadAs(typeof(ShapeBase), typeof(ShapeBase).FullName))
            .Should()
            .Throw<TrainException>($"an abstract type cannot be read into (see {Adr})");

    [TestCase("Some.Assembly.That.Is.Not.Loaded.Output")]
    [TestCase("Some.Assembly.That.Is.Not.Loaded.Output, Some.Assembly")]
    [TestCase("")]
    [TestCase(null)]
    public void ANameMatchingNoLoadedImplementation_IsRefused(string? named) =>
        FluentActions
            .Invoking(() => RemoteRunOutput.ReadAs(typeof(IShape), named))
            .Should()
            .Throw<TrainException>(
                $"a type is never loaded by the name a response gives (see {Adr})"
            );

    [Test]
    public void NoOutput_ReadsAsNull() =>
        RemoteRunOutput.Read(null, typeof(IShape), null).Should().BeNull();
}
