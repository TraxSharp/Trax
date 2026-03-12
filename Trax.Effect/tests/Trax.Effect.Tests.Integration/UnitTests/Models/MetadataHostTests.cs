using System.Text.Json;
using FluentAssertions;
using Trax.Effect.Models.Host;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;

namespace Trax.Effect.Tests.Integration.UnitTests.Models;

[TestFixture]
[NonParallelizable]
public class MetadataHostTests
{
    [TearDown]
    public void TearDown()
    {
        TraxHostInfo.Current = null;
    }

    #region Create

    [Test]
    public void Create_WithHostInfoCurrent_PopulatesHostFields()
    {
        TraxHostInfo.Current = new TraxHostInfo
        {
            HostName = "test-host",
            HostEnvironment = "lambda",
            HostInstanceId = "stream-abc",
            Labels = new Dictionary<string, string> { ["region"] = "us-east-1" },
        };

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "TestTrain",
                ExternalId = Guid.NewGuid().ToString(),
                Input = null,
            }
        );

        metadata.HostName.Should().Be("test-host");
        metadata.HostEnvironment.Should().Be("lambda");
        metadata.HostInstanceId.Should().Be("stream-abc");
        metadata.HostLabels.Should().NotBeNull();
    }

    [Test]
    public void Create_WithoutHostInfoCurrent_HostFieldsAreNull()
    {
        TraxHostInfo.Current = null;

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "TestTrain",
                ExternalId = Guid.NewGuid().ToString(),
                Input = null,
            }
        );

        metadata.HostName.Should().BeNull();
        metadata.HostEnvironment.Should().BeNull();
        metadata.HostInstanceId.Should().BeNull();
        metadata.HostLabels.Should().BeNull();
    }

    [Test]
    public void Create_WithLabels_SerializesAsJson()
    {
        TraxHostInfo.Current = new TraxHostInfo
        {
            HostName = "test-host",
            HostEnvironment = "server",
            HostInstanceId = "test-1",
            Labels = new Dictionary<string, string>
            {
                ["region"] = "us-east-1",
                ["service"] = "api",
            },
        };

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "TestTrain",
                ExternalId = Guid.NewGuid().ToString(),
                Input = null,
            }
        );

        metadata.HostLabels.Should().NotBeNull();
        var labels = JsonSerializer.Deserialize<Dictionary<string, string>>(metadata.HostLabels!);
        labels.Should().ContainKey("region").WhoseValue.Should().Be("us-east-1");
        labels.Should().ContainKey("service").WhoseValue.Should().Be("api");
    }

    [Test]
    public void Create_WithEmptyLabels_HostLabelsIsNull()
    {
        TraxHostInfo.Current = new TraxHostInfo
        {
            HostName = "test-host",
            HostEnvironment = "server",
            HostInstanceId = "test-1",
            Labels = new Dictionary<string, string>(),
        };

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "TestTrain",
                ExternalId = Guid.NewGuid().ToString(),
                Input = null,
            }
        );

        metadata.HostLabels.Should().BeNull();
    }

    #endregion
}
