using FluentAssertions;
using NUnit.Framework;
using Trax.Effect.Attributes;

namespace Trax.Effect.Tests.Integration.UnitTests.Models;

[TestFixture]
public class TraxMutationAttributeTests
{
    [Test]
    public void Constructor_NoArgs_DefaultsToRunOrQueue()
    {
        var attr = new TraxMutationAttribute();

        attr.Operations.Should().Be(GraphQLOperation.Run | GraphQLOperation.Queue);
    }

    [Test]
    public void Constructor_RunOnly_CombinesToRun()
    {
        var attr = new TraxMutationAttribute(GraphQLOperation.Run);

        attr.Operations.Should().Be(GraphQLOperation.Run);
    }

    [Test]
    public void Constructor_QueueOnly_CombinesToQueue()
    {
        var attr = new TraxMutationAttribute(GraphQLOperation.Queue);

        attr.Operations.Should().Be(GraphQLOperation.Queue);
    }

    [Test]
    public void Constructor_RunAndQueue_CombinesViaOr()
    {
        var attr = new TraxMutationAttribute(GraphQLOperation.Run, GraphQLOperation.Queue);

        attr.Operations.Should().Be(GraphQLOperation.Run | GraphQLOperation.Queue);
    }

    [Test]
    public void Properties_SetByInit_ArePersisted()
    {
        var attr = new TraxMutationAttribute(GraphQLOperation.Run)
        {
            Name = "doStuff",
            Description = "does stuff",
            DeprecationReason = "use newDoStuff",
            Namespace = "admin",
        };

        attr.Name.Should().Be("doStuff");
        attr.Description.Should().Be("does stuff");
        attr.DeprecationReason.Should().Be("use newDoStuff");
        attr.Namespace.Should().Be("admin");
    }
}
