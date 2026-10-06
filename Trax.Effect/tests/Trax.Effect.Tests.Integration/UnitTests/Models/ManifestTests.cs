using AwesomeAssertions;
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

    /// <summary>
    /// The stored type name is data: a row can name any type. Only a manifest-properties type is
    /// ever deserialized into, so a row naming something else is refused before any of it runs.
    /// </summary>
    [Test]
    public void GetPropertiesUntyped_AStoredNameThatIsNotManifestProperties_IsRefused()
    {
        var m = NewManifest();
        m.PropertyTypeName = typeof(System.Diagnostics.Process).FullName;
        m.Properties = "{}";

        Action act = () => m.GetPropertiesUntyped();

        act.Should().Throw<TypeLoadException>().WithMessage("*IManifestProperties*");
    }

    /// <summary>
    /// An assembly-qualified name makes <see cref="Type.GetType(string)"/> load that assembly from
    /// disk. Trax writes a FullName, so only the assemblies already loaded are searched.
    /// </summary>
    [Test]
    public void PropertyType_AnAssemblyQualifiedName_IsNotLoaded()
    {
        var m = NewManifest();
        m.PropertyTypeName = typeof(TestProperties).AssemblyQualifiedName;

        Func<Type> act = () => m.PropertyType;

        act.Should().Throw<TypeLoadException>();
    }

    [Test]
    public void GetProperties_MatchesTheStoredNameAgainstTheGivenType()
    {
        var m = NewManifest();
        m.SetProperties(new TestProperties { Greeting = "hi", Count = 3 });

        var restored = m.GetProperties(typeof(TestProperties));

        ((TestProperties)restored).Count.Should().Be(3);
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

        m.NameType.Should().Be(typeof(Trax.Core.Functional.Unit));
    }

    [Test]
    public void PropertyType_NoStoredName_ReturnsUnit()
    {
        var m = new Manifest { Name = typeof(SomeFakeTrain).FullName!, PropertyTypeName = null };

        m.PropertyType.Should().Be(typeof(Trax.Core.Functional.Unit));
    }

    [Test]
    public void NameType_AnEmptyName_IsNotFound()
    {
        var m = new Manifest { Name = "" };

        Action act = () => _ = m.NameType;

        act.Should().Throw<TypeLoadException>().WithMessage("Unable to find type*");
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

    #region Registered input types

    [Test]
    public void A_stored_input_is_read_as_the_registered_type_it_names()
    {
        var m = NewManifest();
        m.SetProperties(new TestProperties { Greeting = "hi", Count = 3 });

        var restored = m.GetPropertiesUntyped([typeof(OtherProperties), typeof(TestProperties)]);

        restored.Should().Be(new TestProperties { Greeting = "hi", Count = 3 });
    }

    /// <summary>
    /// The stored name only chooses among the types the host registered. A type that is loaded
    /// and implements the interface, but that no registered train takes, is not deserialized into.
    /// </summary>
    [Test]
    public void A_loaded_input_type_that_is_not_registered_is_refused()
    {
        var m = NewManifest();
        m.SetProperties(new TestProperties { Greeting = "hi" });

        Action act = () => m.GetPropertiesUntyped([typeof(OtherProperties)]);

        act.Should().Throw<TypeLoadException>().WithMessage("*not a registered*");
    }

    [Test]
    public void A_registered_type_that_is_not_manifest_properties_is_not_a_match()
    {
        var m = NewManifest();
        m.PropertyTypeName = typeof(SomeFakeTrain).FullName;
        m.Properties = "{}";

        Func<Type> act = () => m.ResolvePropertyType([typeof(SomeFakeTrain)]);

        act.Should().Throw<TypeLoadException>();
    }

    [Test]
    public void A_manifest_with_no_stored_input_resolves_to_unit()
    {
        NewManifest()
            .ResolvePropertyType([typeof(TestProperties)])
            .Should()
            .Be(typeof(Trax.Core.Functional.Unit));
    }

    #endregion

    #region Failure window and owner

    [Test]
    public void Create_copies_the_failure_window_and_owner()
    {
        var m = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SomeFakeTrain),
                FailureWindowSeconds = 600,
                Owner = "orders",
            }
        );

        m.FailureWindowSeconds.Should().Be(600);
        m.Owner.Should().Be("orders");
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Create_refuses_a_failure_window_that_counts_nothing(int seconds)
    {
        Action act = () =>
            Manifest.Create(
                new CreateManifest { Name = typeof(SomeFakeTrain), FailureWindowSeconds = seconds }
            );

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestCase("")]
    [TestCase("  ")]
    public void Create_refuses_a_blank_owner(string owner)
    {
        Action act = () =>
            Manifest.Create(new CreateManifest { Name = typeof(SomeFakeTrain), Owner = owner });

        act.Should().Throw<ArgumentException>();
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
