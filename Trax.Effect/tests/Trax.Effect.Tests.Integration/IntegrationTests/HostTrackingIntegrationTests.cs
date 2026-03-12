using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.Host;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

[TestFixture]
[NonParallelizable]
public class HostTrackingIntegrationTests : TestSetup
{
    [TearDown]
    public void ResetHostInfo()
    {
        TraxHostInfo.Current = null;
    }

    #region Persistence

    [Test]
    public async Task Metadata_HostFields_RoundTripThroughPostgres()
    {
        TraxHostInfo.Current = new TraxHostInfo
        {
            HostName = "test-host",
            HostEnvironment = "lambda",
            HostInstanceId = "stream-abc-123",
            Labels = new Dictionary<string, string>(),
        };

        using var context = (IDataContext)DataContextFactory.Create();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "HostTrackingTest",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var reloaded = await context.Metadatas.AsNoTracking().FirstAsync(m => m.Id == metadata.Id);

        reloaded.HostName.Should().Be("test-host");
        reloaded.HostEnvironment.Should().Be("lambda");
        reloaded.HostInstanceId.Should().Be("stream-abc-123");
    }

    [Test]
    public async Task Metadata_HostLabels_PersistedAsJsonb()
    {
        TraxHostInfo.Current = new TraxHostInfo
        {
            HostName = "test-host",
            HostEnvironment = "ecs",
            HostInstanceId = "task-456",
            Labels = new Dictionary<string, string>
            {
                ["region"] = "us-east-1",
                ["service"] = "content-shield",
            },
        };

        using var context = (IDataContext)DataContextFactory.Create();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "HostLabelsTest",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var reloaded = await context.Metadatas.AsNoTracking().FirstAsync(m => m.Id == metadata.Id);

        reloaded.HostLabels.Should().NotBeNull();
        var labels = JsonSerializer.Deserialize<Dictionary<string, string>>(reloaded.HostLabels!);
        labels.Should().ContainKey("region").WhoseValue.Should().Be("us-east-1");
        labels.Should().ContainKey("service").WhoseValue.Should().Be("content-shield");
    }

    [Test]
    public async Task Metadata_NullHostLabels_StoresNull()
    {
        TraxHostInfo.Current = new TraxHostInfo
        {
            HostName = "test-host",
            HostEnvironment = "server",
            HostInstanceId = "test-1",
            Labels = new Dictionary<string, string>(),
        };

        using var context = (IDataContext)DataContextFactory.Create();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "NullLabelsTest",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var reloaded = await context.Metadatas.AsNoTracking().FirstAsync(m => m.Id == metadata.Id);

        reloaded.HostLabels.Should().BeNull();
    }

    [Test]
    public async Task Metadata_HostFields_QueryableByHostName()
    {
        TraxHostInfo.Current = new TraxHostInfo
        {
            HostName = "host-alpha",
            HostEnvironment = "lambda",
            HostInstanceId = "stream-1",
            Labels = new Dictionary<string, string>(),
        };

        using var context = (IDataContext)DataContextFactory.Create();

        var metadata1 = Metadata.Create(
            new CreateMetadata
            {
                Name = "QueryTest1",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        await context.Track(metadata1);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        TraxHostInfo.Current = new TraxHostInfo
        {
            HostName = "host-beta",
            HostEnvironment = "ecs",
            HostInstanceId = "task-2",
            Labels = new Dictionary<string, string>(),
        };

        var metadata2 = Metadata.Create(
            new CreateMetadata
            {
                Name = "QueryTest2",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        await context.Track(metadata2);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var results = await context
            .Metadatas.AsNoTracking()
            .Where(m => m.HostName == "host-alpha")
            .ToListAsync();

        results.Should().HaveCount(1);
        results[0].Id.Should().Be(metadata1.Id);
    }

    [Test]
    public async Task Metadata_HostFields_QueryableByHostEnvironment()
    {
        TraxHostInfo.Current = new TraxHostInfo
        {
            HostName = "host-1",
            HostEnvironment = "lambda",
            HostInstanceId = "stream-a",
            Labels = new Dictionary<string, string>(),
        };

        using var context = (IDataContext)DataContextFactory.Create();

        var metadata1 = Metadata.Create(
            new CreateMetadata
            {
                Name = "EnvQueryTest1",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        await context.Track(metadata1);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        TraxHostInfo.Current = new TraxHostInfo
        {
            HostName = "host-2",
            HostEnvironment = "ecs",
            HostInstanceId = "task-b",
            Labels = new Dictionary<string, string>(),
        };

        var metadata2 = Metadata.Create(
            new CreateMetadata
            {
                Name = "EnvQueryTest2",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        await context.Track(metadata2);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var lambdaResults = await context
            .Metadatas.AsNoTracking()
            .Where(m => m.HostEnvironment == "lambda")
            .ToListAsync();

        lambdaResults.Should().HaveCount(1);
        lambdaResults[0].HostName.Should().Be("host-1");
    }

    #endregion
}
