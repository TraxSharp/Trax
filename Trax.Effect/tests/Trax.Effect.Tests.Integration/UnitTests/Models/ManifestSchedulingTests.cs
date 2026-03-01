using System.Text.Json.Nodes;
using FluentAssertions;
using LanguageExt;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;

namespace Trax.Effect.Tests.Integration.UnitTests.Models;

[TestFixture]
public class ManifestSchedulingTests
{
    [Test]
    public void Create_WithDefaults_ShouldHaveCorrectSchedulingDefaults()
    {
        // Arrange & Act
        var manifest = Manifest.Create(new CreateManifest { Name = typeof(Unit) });

        // Assert
        manifest.IsEnabled.Should().BeTrue();
        manifest.ScheduleType.Should().Be(ScheduleType.None);
        manifest.CronExpression.Should().BeNull();
        manifest.IntervalSeconds.Should().BeNull();
        manifest.MaxRetries.Should().Be(3);
        manifest.TimeoutSeconds.Should().BeNull();
        manifest.LastSuccessfulRun.Should().BeNull();
    }

    [Test]
    public void Create_ShouldGenerateNonEmptyExternalId()
    {
        // Arrange & Act
        var manifest = Manifest.Create(new CreateManifest { Name = typeof(Unit) });

        // Assert
        manifest.ExternalId.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void Create_ShouldGenerateUniqueExternalIds()
    {
        // Arrange & Act
        var manifest1 = Manifest.Create(new CreateManifest { Name = typeof(Unit) });
        var manifest2 = Manifest.Create(new CreateManifest { Name = typeof(Unit) });

        // Assert
        manifest1.ExternalId.Should().NotBe(manifest2.ExternalId);
    }

    [Test]
    public void ExternalId_ShouldBeSettableToArbitraryString()
    {
        // Arrange
        var manifest = Manifest.Create(new CreateManifest { Name = typeof(Unit) });
        var customId = "custom-slug-identifier-that-is-not-a-guid";

        // Act
        manifest.ExternalId = customId;

        // Assert
        manifest.ExternalId.Should().Be(customId);
    }

    [Test]
    public void Create_WithCronSchedule_ShouldSetCronProperties()
    {
        // Arrange & Act
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(Unit),
                ScheduleType = ScheduleType.Cron,
                CronExpression = "0 3 * * *", // Daily at 3am
            }
        );

        // Assert
        manifest.ScheduleType.Should().Be(ScheduleType.Cron);
        manifest.CronExpression.Should().Be("0 3 * * *");
        manifest.IntervalSeconds.Should().BeNull();
    }

    [Test]
    public void Create_WithIntervalSchedule_ShouldSetIntervalProperties()
    {
        // Arrange & Act
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(Unit),
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 300, // Every 5 minutes
            }
        );

        // Assert
        manifest.ScheduleType.Should().Be(ScheduleType.Interval);
        manifest.IntervalSeconds.Should().Be(300);
        manifest.CronExpression.Should().BeNull();
    }

    [Test]
    public void Create_WithOnDemandSchedule_ShouldSetScheduleType()
    {
        // Arrange & Act
        var manifest = Manifest.Create(
            new CreateManifest { Name = typeof(Unit), ScheduleType = ScheduleType.OnDemand }
        );

        // Assert
        manifest.ScheduleType.Should().Be(ScheduleType.OnDemand);
    }

    [Test]
    public void Create_WithDisabled_ShouldSetIsEnabledFalse()
    {
        // Arrange & Act
        var manifest = Manifest.Create(
            new CreateManifest { Name = typeof(Unit), IsEnabled = false }
        );

        // Assert
        manifest.IsEnabled.Should().BeFalse();
    }

    [Test]
    public void Create_WithCustomMaxRetries_ShouldSetMaxRetries()
    {
        // Arrange & Act
        var manifest = Manifest.Create(new CreateManifest { Name = typeof(Unit), MaxRetries = 10 });

        // Assert
        manifest.MaxRetries.Should().Be(10);
    }

    [Test]
    public void Create_WithZeroMaxRetries_ShouldAllowZero()
    {
        // Arrange & Act
        var manifest = Manifest.Create(new CreateManifest { Name = typeof(Unit), MaxRetries = 0 });

        // Assert
        manifest.MaxRetries.Should().Be(0);
    }

    [Test]
    public void Create_WithTimeout_ShouldSetTimeoutSeconds()
    {
        // Arrange & Act
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(Unit),
                TimeoutSeconds = 3600, // 1 hour
            }
        );

        // Assert
        manifest.TimeoutSeconds.Should().Be(3600);
    }

    [Test]
    public void Create_WithAllSchedulingProperties_ShouldSetAllProperties()
    {
        // Arrange & Act
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(Unit),
                IsEnabled = true,
                ScheduleType = ScheduleType.Cron,
                CronExpression = "0 */6 * * *", // Every 6 hours
                MaxRetries = 5,
                TimeoutSeconds = 7200,
            }
        );

        // Assert
        manifest.IsEnabled.Should().BeTrue();
        manifest.ScheduleType.Should().Be(ScheduleType.Cron);
        manifest.CronExpression.Should().Be("0 */6 * * *");
        manifest.MaxRetries.Should().Be(5);
        manifest.TimeoutSeconds.Should().Be(7200);
    }

    [Test]
    public void LastSuccessfulRun_ShouldBeSettable()
    {
        // Arrange
        var manifest = Manifest.Create(new CreateManifest { Name = typeof(Unit) });
        var successTime = DateTime.UtcNow;

        // Act
        manifest.LastSuccessfulRun = successTime;

        // Assert
        manifest.LastSuccessfulRun.Should().Be(successTime);
    }

    [Test]
    public void ScheduleType_Enum_ShouldHaveCorrectValues()
    {
        // Assert enum values are as expected
        ((int)ScheduleType.None)
            .Should()
            .Be(0);
        ((int)ScheduleType.Cron).Should().Be(1);
        ((int)ScheduleType.Interval).Should().Be(2);
        ((int)ScheduleType.OnDemand).Should().Be(3);
    }

    #region Properties Serialization Tests

    [Test]
    public void SetProperties_ShouldProduceTypeDiscriminator()
    {
        // Arrange
        var config = new TestManifestProperties
        {
            Name = "TypeDiscriminatorTest",
            Value = 42,
            Enabled = true,
        };

        // Act
        var manifest = Manifest.Create(
            new CreateManifest { Name = typeof(Unit), Properties = config }
        );

        // Assert - JSON should contain $type and NOT contain $id/$values
        manifest.Properties.Should().NotBeNull();
        manifest.Properties.Should().Contain("\"$type\"");
        manifest.Properties.Should().Contain(typeof(TestManifestProperties).FullName!);
        manifest.Properties.Should().NotContain("\"$id\"");
        manifest.Properties.Should().NotContain("\"$values\"");
    }

    [Test]
    public void SetProperties_ShouldPlaceTypeAsFirstProperty()
    {
        // Arrange
        var config = new TestManifestProperties
        {
            Name = "Order",
            Value = 1,
            Enabled = true,
        };

        // Act
        var manifest = Manifest.Create(
            new CreateManifest { Name = typeof(Unit), Properties = config }
        );

        // Assert - $type should be the first key in the JSON
        var jsonObj = JsonNode.Parse(manifest.Properties!)!.AsObject();
        jsonObj.First().Key.Should().Be("$type");
    }

    [Test]
    public void GetProperties_ShouldRoundTripEnumValues()
    {
        // Arrange
        var config = new TestManifestPropertiesWithEnum
        {
            Name = "EnumTest",
            Category = TestCategory.Beta,
            Values = [1, 2, 3],
        };

        var manifest = Manifest.Create(
            new CreateManifest { Name = typeof(Unit), Properties = config }
        );

        // Act
        var retrieved = manifest.GetProperties<TestManifestPropertiesWithEnum>();

        // Assert
        retrieved.Name.Should().Be("EnumTest");
        retrieved.Category.Should().Be(TestCategory.Beta);
        retrieved.Values.Should().BeEquivalentTo([1, 2, 3]);
    }

    [Test]
    public void GetPropertiesUntyped_ShouldReturnCorrectRuntimeType()
    {
        // Arrange
        var config = new TestManifestPropertiesWithEnum
        {
            Name = "UntypedTest",
            Category = TestCategory.Gamma,
            Values = [10, 20],
        };

        var manifest = Manifest.Create(
            new CreateManifest { Name = typeof(Unit), Properties = config }
        );

        // Act
        var result = manifest.GetPropertiesUntyped();

        // Assert
        result.Should().BeOfType<TestManifestPropertiesWithEnum>();
        var typed = (TestManifestPropertiesWithEnum)result;
        typed.Name.Should().Be("UntypedTest");
        typed.Category.Should().Be(TestCategory.Gamma);
        typed.Values.Should().BeEquivalentTo([10, 20]);
    }

    [Test]
    public void GetProperties_ShouldRoundTripListFields()
    {
        // Arrange
        var config = new TestManifestPropertiesWithEnum
        {
            Name = "ListTest",
            Category = TestCategory.Alpha,
            Values = [100, 200, 300, 400, 500],
        };

        var manifest = Manifest.Create(
            new CreateManifest { Name = typeof(Unit), Properties = config }
        );

        // Act
        var retrieved = manifest.GetProperties<TestManifestPropertiesWithEnum>();

        // Assert
        retrieved.Values.Should().HaveCount(5);
        retrieved.Values.Should().BeEquivalentTo([100, 200, 300, 400, 500]);
    }

    [Test]
    public void SetProperties_EnumShouldSerializeAsString()
    {
        // Arrange
        var config = new TestManifestPropertiesWithEnum
        {
            Name = "EnumStringTest",
            Category = TestCategory.Beta,
            Values = [],
        };

        // Act
        var manifest = Manifest.Create(
            new CreateManifest { Name = typeof(Unit), Properties = config }
        );

        // Assert - enum should be serialized as string name, not integer
        manifest.Properties.Should().Contain("\"Beta\"");
        manifest.Properties.Should().NotContain(": 1");
    }

    #endregion

    private class TestManifestProperties : IManifestProperties
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
        public bool Enabled { get; set; }
    }

    private enum TestCategory
    {
        Alpha,
        Beta,
        Gamma,
    }

    private class TestManifestPropertiesWithEnum : IManifestProperties
    {
        public string Name { get; set; } = string.Empty;
        public TestCategory Category { get; set; }
        public List<int> Values { get; set; } = [];
    }

    #region Record with Custom Enum Serialization Tests

    /// <summary>
    /// Simulates an arbitrary custom enum type like NetSuiteTable
    /// </summary>
    private enum NetSuiteTable
    {
        Customers,
        Orders,
        Inventory,
        Invoices,
    }

    /// <summary>
    /// Record type similar to the user's ExtractRequest pattern.
    /// Uses primary constructor with properties and a field with default value.
    /// </summary>
    private record ExtractRequest(NetSuiteTable Table, int Batch, List<int>? Ids = null)
        : IManifestProperties
    {
        public NetSuiteTable Table { get; } = Table;
        public int Batch { get; } = Batch;
        public List<int> Ids = Ids ?? [];

        public static ExtractRequest Create(
            NetSuiteTable table,
            int batch,
            List<int>? ids = null
        ) => new(table, batch, ids);
    }

    [Test]
    public void SetProperties_RecordWithCustomEnum_ShouldSerializeEnumAsString()
    {
        // Arrange
        var request = ExtractRequest.Create(NetSuiteTable.Orders, 100);

        // Act
        var manifest = Manifest.Create(
            new CreateManifest { Name = typeof(Unit), Properties = request }
        );

        // Assert - enum should be serialized as string name, not integer
        manifest.Properties.Should().NotBeNull();
        manifest.Properties.Should().Contain("\"Orders\"");
        // Verify table is serialized as string "Orders" not as integer "1"
        manifest.Properties.Should().NotContain("\"table\": 1");
    }

    [Test]
    public void SetProperties_RecordWithCustomEnum_ShouldContainTypeDiscriminator()
    {
        // Arrange
        var request = ExtractRequest.Create(NetSuiteTable.Customers, 50);

        // Act
        var manifest = Manifest.Create(
            new CreateManifest { Name = typeof(Unit), Properties = request }
        );

        // Assert
        manifest.Properties.Should().Contain("\"$type\"");
        manifest.Properties.Should().Contain(typeof(ExtractRequest).FullName!);
    }

    [Test]
    public void GetProperties_RecordWithCustomEnum_ShouldRoundTripCorrectly()
    {
        // Arrange
        var original = ExtractRequest.Create(NetSuiteTable.Inventory, 200, [1, 2, 3]);

        var manifest = Manifest.Create(
            new CreateManifest { Name = typeof(Unit), Properties = original }
        );

        // Act
        var retrieved = manifest.GetProperties<ExtractRequest>();

        // Assert
        retrieved.Table.Should().Be(NetSuiteTable.Inventory);
        retrieved.Batch.Should().Be(200);
    }

    [Test]
    public void GetProperties_RecordWithCustomEnum_ShouldPreserveAllEnumValues()
    {
        // Test each enum value serializes and deserializes correctly
        foreach (var tableValue in Enum.GetValues<NetSuiteTable>())
        {
            // Arrange
            var request = ExtractRequest.Create(tableValue, 1);

            var manifest = Manifest.Create(
                new CreateManifest { Name = typeof(Unit), Properties = request }
            );

            // Act
            var retrieved = manifest.GetProperties<ExtractRequest>();

            // Assert
            retrieved
                .Table.Should()
                .Be(tableValue, $"Enum value {tableValue} should round-trip correctly");
        }
    }

    [Test]
    public void SetProperties_RecordWithNullableList_ShouldHandleNullAsEmptyList()
    {
        // Arrange - create with null Ids (should default to empty list)
        var request = ExtractRequest.Create(NetSuiteTable.Orders, 50, null);

        var manifest = Manifest.Create(
            new CreateManifest { Name = typeof(Unit), Properties = request }
        );

        // Act
        var retrieved = manifest.GetProperties<ExtractRequest>();

        // Assert
        retrieved.Ids.Should().NotBeNull();
    }

    [Test]
    public void SetProperties_RecordWithPopulatedList_ShouldRoundTripListValues()
    {
        // Arrange
        var ids = new List<int> { 100, 200, 300, 400, 500 };
        var request = ExtractRequest.Create(NetSuiteTable.Invoices, 25, ids);

        var manifest = Manifest.Create(
            new CreateManifest { Name = typeof(Unit), Properties = request }
        );

        // Act
        var retrieved = manifest.GetProperties<ExtractRequest>();

        // Assert
        retrieved.Ids.Should().NotBeNull();
    }

    [Test]
    public void SetProperties_RecordWithCustomEnum_JsonShouldBeValidForDatabaseStorage()
    {
        // Arrange
        var request = ExtractRequest.Create(NetSuiteTable.Customers, 100, [1, 2, 3]);

        // Act
        var manifest = Manifest.Create(
            new CreateManifest { Name = typeof(Unit), Properties = request }
        );

        // Assert - JSON should be parseable and well-formed
        var parseAction = () => JsonNode.Parse(manifest.Properties!);
        parseAction.Should().NotThrow();

        var json = JsonNode.Parse(manifest.Properties!)!.AsObject();

        // Should have $type as first property
        json.First().Key.Should().Be("$type");

        // Should contain expected property names (camelCase in JSON)
        json.ContainsKey("table").Should().BeTrue();
        json.ContainsKey("batch").Should().BeTrue();
    }

    [Test]
    public void GetPropertiesUntyped_RecordWithCustomEnum_ShouldReturnCorrectType()
    {
        // Arrange
        var request = ExtractRequest.Create(NetSuiteTable.Orders, 75, [10, 20]);

        var manifest = Manifest.Create(
            new CreateManifest { Name = typeof(Unit), Properties = request }
        );

        // Act
        var result = manifest.GetPropertiesUntyped();

        // Assert
        result.Should().BeOfType<ExtractRequest>();
        var typed = (ExtractRequest)result;
        typed.Table.Should().Be(NetSuiteTable.Orders);
        typed.Batch.Should().Be(75);
    }

    #endregion
}
