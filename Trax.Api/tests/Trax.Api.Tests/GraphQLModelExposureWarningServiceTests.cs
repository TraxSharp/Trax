using System.ComponentModel.DataAnnotations.Schema;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Startup;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

/// <summary>
/// Coverage for the silent-success early-return paths in
/// <see cref="GraphQLModelExposureWarningService"/>. The warning's existence
/// is exercised wherever an ungated model is registered (e.g.
/// <c>ModelQueryAuthE2ETests</c>), but the early-out paths — no models at all,
/// or every model gated — never log. A regression that removes those early
/// returns would emit spurious warnings on well-configured hosts; these tests
/// pin the contract.
/// </summary>
[TestFixture]
public class GraphQLModelExposureWarningServiceTests
{
    [Test]
    public async Task StartAsync_NoModelRegistrations_LogsNothing()
    {
        // Host that registers no [TraxQueryModel] entities at all. The
        // warning service must short-circuit before computing anything.
        var services = new ServiceCollection();
        var config = new TraxGraphQLBuilder(services).Build();
        config.ModelRegistrations.Should().BeEmpty();

        var logger = new RecordingLogger<GraphQLModelExposureWarningService>();
        var sut = new GraphQLModelExposureWarningService(config, logger);

        await sut.StartAsync(CancellationToken.None);

        logger.Entries.Should().BeEmpty();
    }

    [Test]
    public async Task StartAsync_AllRegistrationsGated_LogsNothing()
    {
        // Every registered entity carries [TraxAuthorize] — there's no
        // ungated surface to warn about. The warning would be misleading
        // ("Trax GraphQL: 0 of N model query registration(s) carry no
        // [TraxAuthorize]") so the service must skip emission entirely.
        var services = new ServiceCollection();
        var config = new TraxGraphQLBuilder(services).AddDbContext<AllGatedDbContext>().Build();
        config.ModelRegistrations.Should().NotBeEmpty();
        config
            .ModelRegistrations.All(r => r.AuthorizeAttributes.Count > 0)
            .Should()
            .BeTrue("the fixture must register only gated entities");

        var logger = new RecordingLogger<GraphQLModelExposureWarningService>();
        var sut = new GraphQLModelExposureWarningService(config, logger);

        await sut.StartAsync(CancellationToken.None);

        logger.Entries.Should().BeEmpty();
    }

    [Test]
    public async Task StartAsync_SomeUngated_LogsWarningWithBothCounts()
    {
        // One gated + one ungated. The warning message must name both the
        // ungated count and the total — that's how a reviewer of host logs
        // distinguishes "I forgot to gate one entity" from "the gate isn't
        // wired at all."
        var services = new ServiceCollection();
        var config = new TraxGraphQLBuilder(services).AddDbContext<MixedDbContext>().Build();
        config.ModelRegistrations.Should().HaveCount(2);

        var logger = new RecordingLogger<GraphQLModelExposureWarningService>();
        var sut = new GraphQLModelExposureWarningService(config, logger);

        await sut.StartAsync(CancellationToken.None);

        logger.Entries.Should().HaveCount(1);
        var entry = logger.Entries.Single();
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("model query registration");
        // Count tokens get materialised via structured-log argument formatting;
        // assert both 1 (ungated) and 2 (total) appear somewhere in the rendered
        // body so a refactor that drops one count is caught.
        entry.RenderedMessage.Should().Contain("1");
        entry.RenderedMessage.Should().Contain("2");
    }

    [Test]
    public async Task StopAsync_CompletesSynchronously()
    {
        // The warning service owns no resources. Pin the IHostedService
        // contract — a future refactor that adds async cleanup to release
        // something must update this test instead of silently changing the
        // host-shutdown shape.
        var services = new ServiceCollection();
        var config = new TraxGraphQLBuilder(services).Build();
        var logger = new RecordingLogger<GraphQLModelExposureWarningService>();
        var sut = new GraphQLModelExposureWarningService(config, logger);

        var task = sut.StopAsync(CancellationToken.None);

        task.IsCompletedSuccessfully.Should().BeTrue();
        await task;
    }

    // ── Fixture entities ────────────────────────────────────────────────

    [TraxQueryModel]
    [TraxAuthorize(Roles = "Admin")]
    [Table("gated_a", Schema = "test_warn")]
    private class GatedA
    {
        [Column("id")]
        public long Id { get; set; }
    }

    [TraxQueryModel]
    [TraxAuthorize(Roles = "Admin")]
    [Table("gated_b", Schema = "test_warn")]
    private class GatedB
    {
        [Column("id")]
        public long Id { get; set; }
    }

    [TraxQueryModel]
    [Table("ungated", Schema = "test_warn")]
    private class Ungated
    {
        [Column("id")]
        public long Id { get; set; }
    }

    private class AllGatedDbContext(DbContextOptions<AllGatedDbContext> options)
        : DbContext(options)
    {
        public DbSet<GatedA> A { get; set; } = null!;
        public DbSet<GatedB> B { get; set; } = null!;
    }

    private class MixedDbContext(DbContextOptions<MixedDbContext> options) : DbContext(options)
    {
        public DbSet<GatedA> Gated { get; set; } = null!;
        public DbSet<Ungated> Ungated { get; set; } = null!;
    }

    // ── Logger ──────────────────────────────────────────────────────────

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add(
                new LogEntry
                {
                    Level = logLevel,
                    Message = state?.ToString() ?? string.Empty,
                    RenderedMessage = formatter(state, exception),
                }
            );
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose() { }
        }
    }

    private sealed class LogEntry
    {
        public LogLevel Level { get; init; }
        public string Message { get; init; } = "";
        public string RenderedMessage { get; init; } = "";
    }
}
