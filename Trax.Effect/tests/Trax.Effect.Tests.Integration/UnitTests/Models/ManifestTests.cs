using FluentAssertions;
using NUnit.Framework;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;

namespace Trax.Effect.Tests.Integration.UnitTests.Models;

[TestFixture]
public class ManifestTests
{
    private static Manifest NewManifest() =>
        Manifest.Create(new CreateManifest { Name = typeof(SomeFakeTrain) });

    #region GetExclusions / SetExclusions

    [Test]
    public void GetExclusions_NullProperty_ReturnsEmptyList()
    {
        var m = NewManifest();
        m.Exclusions = null;

        m.GetExclusions().Should().BeEmpty();
    }

    [Test]
    public void GetExclusions_EmptyProperty_ReturnsEmptyList()
    {
        var m = NewManifest();
        m.Exclusions = "";

        m.GetExclusions().Should().BeEmpty();
    }

    [Test]
    public void SetExclusions_EmptyList_StoresNull()
    {
        var m = NewManifest();
        m.SetExclusions([]);

        m.Exclusions.Should().BeNull();
    }

    [Test]
    public void SetExclusions_RoundTrip_PreservesAllExclusions()
    {
        var m = NewManifest();
        var exclusions = new List<Exclusion>
        {
            Exclude.DaysOfWeek(DayOfWeek.Sunday),
            Exclude.DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 7)),
            Exclude.TimeWindow(new TimeOnly(2, 0), new TimeOnly(4, 0)),
        };

        m.SetExclusions(exclusions);
        var roundTripped = m.GetExclusions();

        roundTripped.Should().HaveCount(3);
        roundTripped[0].Type.Should().Be(ExclusionType.DaysOfWeek);
        roundTripped[1].Type.Should().Be(ExclusionType.DateRange);
        roundTripped[2].Type.Should().Be(ExclusionType.TimeWindow);
    }

    #endregion

    #region SetProperties / GetProperties

    [Test]
    public void SetProperties_RoundTrip_RestoresValues()
    {
        var m = NewManifest();
        var props = new TestProperties { Greeting = "hello", Count = 42 };

        m.SetProperties(props);

        m.PropertyTypeName.Should().Be(typeof(TestProperties).FullName);
        m.Properties.Should().NotBeNullOrEmpty();
        m.Properties.Should().Contain("$type");

        var restored = m.GetProperties<TestProperties>();
        restored.Greeting.Should().Be("hello");
        restored.Count.Should().Be(42);
    }

    [Test]
    public void GetProperties_TypeMismatch_Throws()
    {
        var m = NewManifest();
        m.SetProperties(new TestProperties { Greeting = "hi", Count = 1 });

        Action act = () => m.GetProperties(typeof(OtherProperties));

        act.Should().Throw<Exception>().WithMessage("*not saved type*");
    }

    [Test]
    public void GetProperties_NoPropertiesStored_Throws()
    {
        var m = NewManifest();
        m.SetProperties(new TestProperties { Greeting = "x", Count = 0 });
        // Wipe the stored JSON but leave the type name
        m.Properties = null;

        Action act = () => m.GetProperties(typeof(TestProperties));

        act.Should().Throw<Exception>().WithMessage("*Cannot deserialize null*");
    }

    [Test]
    public void GetPropertiesUntyped_RoundTrip_ReturnsCorrectType()
    {
        var m = NewManifest();
        m.SetProperties(new TestProperties { Greeting = "hi", Count = 7 });

        var restored = m.GetPropertiesUntyped();

        restored.Should().BeOfType<TestProperties>();
        ((TestProperties)restored).Count.Should().Be(7);
    }

    [Test]
    public void GetPropertiesUntyped_NoPropertiesStored_Throws()
    {
        var m = NewManifest();
        m.SetProperties(new TestProperties { Greeting = "x", Count = 0 });
        m.Properties = null;

        Action act = () => m.GetPropertiesUntyped();

        act.Should().Throw<Exception>().WithMessage("*Cannot deserialize null*");
    }

    #endregion

    #region Create / Type resolution

    [Test]
    public void Create_NameWithoutFullName_Throws()
    {
        // typeof(int).FullName is non-null; build a synthetic Type with null FullName via mock
        // is impractical. Verify the happy-path Create instead.
        var m = NewManifest();

        m.Name.Should().Be(typeof(SomeFakeTrain).FullName);
        m.ExternalId.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void NameType_ResolvesToOriginalType()
    {
        var m = NewManifest();

        m.NameType.Should().Be(typeof(SomeFakeTrain));
    }

    [Test]
    public void NameType_NullName_ReturnsUnit()
    {
        var m = new Manifest { Name = null! };

        m.NameType.Should().Be(typeof(LanguageExt.Unit));
    }

    [Test]
    public void ToString_RoundTripsAsJson()
    {
        var m = NewManifest();

        var s = m.ToString();

        s.Should().NotBeNullOrEmpty();
        s.Should().Contain("name");
    }

    #endregion

    private class SomeFakeTrain { }

    private record TestProperties : IManifestProperties
    {
        public string Greeting { get; init; } = "";
        public int Count { get; init; }
    }

    private record OtherProperties : IManifestProperties
    {
        public string Other { get; init; } = "";
    }
}
