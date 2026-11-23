using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;

namespace Trax.StateMachine.Tests.UnitTests;

internal static class PersonKeys
{
    internal static InstanceKey Key(this Person person) => new("order", person.Name);
}
