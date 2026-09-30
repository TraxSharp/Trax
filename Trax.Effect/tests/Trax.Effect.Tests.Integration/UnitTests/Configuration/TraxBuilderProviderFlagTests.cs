using System.Reflection;
using FluentAssertions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Configuration.TraxEffectBuilder;

namespace Trax.Effect.Tests.Integration.UnitTests.Configuration;

/// <summary>
/// The flags that build-time validation reads are set only by the calls that earn them: a data
/// provider's <c>Use*</c> method, or <c>AddMediator</c>. A consumer that could set one directly
/// would pass the validation without the thing it checks for.
/// </summary>
[TestFixture]
public class TraxBuilderProviderFlagTests
{
    private static IEnumerable<TestCaseData> Flags()
    {
        yield return new TestCaseData(typeof(TraxBuilder), nameof(TraxBuilder.HasDatabaseProvider));
        yield return new TestCaseData(typeof(TraxBuilder), nameof(TraxBuilder.HasDataProvider));
        yield return new TestCaseData(typeof(TraxBuilder), nameof(TraxBuilder.MediatorConfigured));
        yield return new TestCaseData(
            typeof(TraxEffectBuilder),
            nameof(TraxEffectBuilder.HasDatabaseProvider)
        );
        yield return new TestCaseData(
            typeof(TraxEffectBuilder),
            nameof(TraxEffectBuilder.HasDataProvider)
        );
    }

    [TestCaseSource(nameof(Flags))]
    public void AValidationFlag_CanBeReadButNotSetByAConsumer(Type builder, string flag)
    {
        var property = builder.GetProperty(flag, BindingFlags.Public | BindingFlags.Instance)!;

        property.GetGetMethod().Should().NotBeNull("consumers still read the flag");
        property
            .GetSetMethod(nonPublic: false)
            .Should()
            .BeNull($"setting {builder.Name}.{flag} directly would pass fail-closed validation");
    }
}
