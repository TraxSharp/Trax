using FluentAssertions;
using Trax.Mediator.Services.TrainRegistry;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Tests.UnitTests;

/// <summary>
/// The name matching every stored or received input type name goes through: only a registered
/// train's input resolves.
///
/// <para>Enforces <c>docs/adr/0006-a-runner-requires-an-authorization-posture.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0006-a-runner-requires-an-authorization-posture.md")]
[TestFixture]
public class RegisteredInputTypesTests
{
    public sealed record RegisteredInput(string Value);

    public sealed record OtherInput(string Value);

    private sealed class Registry : ITrainRegistry
    {
        public Dictionary<Type, Type> InputTypeToTrain { get; set; } =
            new() { [typeof(RegisteredInput)] = typeof(object) };
    }

    private static readonly Registry TheRegistry = new();

    [Test]
    public void Finds_a_registered_input_by_full_name() =>
        RegisteredInputTypes
            .Find(TheRegistry, typeof(RegisteredInput).FullName!)
            .Should()
            .Be(typeof(RegisteredInput));

    [Test]
    public void Finds_a_registered_input_by_assembly_qualified_name() =>
        RegisteredInputTypes
            .Find(TheRegistry, typeof(RegisteredInput).AssemblyQualifiedName!)
            .Should()
            .Be(typeof(RegisteredInput));

    [Test]
    public void Ignores_the_assembly_version_of_an_assembly_qualified_name()
    {
        var assembly = typeof(RegisteredInput).Assembly.GetName().Name;
        var name =
            $"{typeof(RegisteredInput).FullName}, {assembly}, Version=0.0.0.1, Culture=neutral, PublicKeyToken=null";

        RegisteredInputTypes.Find(TheRegistry, name).Should().Be(typeof(RegisteredInput));
    }

    [TestCase("Some.Other.Assembly")]
    [TestCase("")]
    public void Refuses_an_assembly_qualified_name_in_another_assembly(string assembly) =>
        RegisteredInputTypes
            .Find(TheRegistry, $"{typeof(RegisteredInput).FullName}, {assembly}")
            .Should()
            .BeNull();

    [Test]
    public void Refuses_a_loaded_type_that_is_not_a_registered_input()
    {
        RegisteredInputTypes
            .Find(TheRegistry, typeof(OtherInput).FullName!)
            .Should()
            .BeNull(
                "only a registered train's input resolves (see docs/adr/0006-a-runner-requires-an-authorization-posture.md)"
            );
        RegisteredInputTypes.Find(TheRegistry, typeof(string).FullName!).Should().BeNull();
    }

    [Test]
    public void Refuses_a_name_that_only_starts_with_a_registered_input() =>
        RegisteredInputTypes
            .Find(TheRegistry, typeof(RegisteredInput).FullName + "Extra")
            .Should()
            .BeNull();
}
