using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.EnqueueContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Tests.Postgres.Integration.Fixtures;

namespace Trax.Mediator.Tests.Postgres.Integration.IntegrationTests;

/// <summary>
/// What an enqueue does with what it is given: no input, a scheduled time in any kind, and
/// several enqueues sharing one scope. Each of these used to be accepted and then fail somewhere
/// the caller could not see, or fail for reasons unrelated to the caller.
/// </summary>
[TestFixture]
public class QueueInputTests : TestSetup
{
    private ITrainExecutionService Execution =>
        Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

    private async Task<WorkQueue> EntryAsync(long id)
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        return await context.WorkQueues.AsNoTracking().SingleAsync(w => w.Id == id);
    }

    [Test]
    public async Task No_input_is_stored_as_an_empty_object_the_runner_can_use()
    {
        var result = await Execution.QueueAsync(typeof(IOptionalInputTrain).FullName!, null);

        var stored = (await EntryAsync(result.WorkQueueId)).Input;

        stored
            .Should()
            .NotBeNull("the runner refuses an entry with no input, so null could only fail later");
        JsonSerializer.Deserialize<OptionalInput>(stored!)!.Label.Should().Be("default");
    }

    [Test]
    public async Task An_input_that_cannot_be_built_from_nothing_is_refused_at_enqueue()
    {
        var act = async () =>
            await Execution.QueueAsync(typeof(IRequiredInputTrain).FullName!, null);

        await act.Should()
            .ThrowAsync<JsonException>(
                "the caller is the one who can supply the missing value, so it fails for them"
            );
    }

    [Test]
    public async Task A_positional_record_input_is_refused_when_none_was_given()
    {
        // System.Text.Json builds a positional record from {} with every parameter at its
        // default, so without the check this would queue a run whose Id is null.
        var before = await WorkQueueCountAsync();

        var act = async () =>
            await Execution.QueueAsync(typeof(IPositionalInputTrain).FullName!, null);

        (await act.Should().ThrowAsync<JsonException>()).WithMessage(
            "No input was given*PositionalInput*",
            "the caller is the one who can supply the missing values"
        );
        (await WorkQueueCountAsync()).Should().Be(before, "a refused enqueue writes nothing");
    }

    [Test]
    public async Task A_positional_record_whose_parameters_have_defaults_can_be_queued_without_input()
    {
        var result = await Execution.QueueAsync(
            typeof(IDefaultedPositionalInputTrain).FullName!,
            ""
        );

        var stored = (await EntryAsync(result.WorkQueueId)).Input;

        JsonSerializer.Deserialize<DefaultedPositionalInput>(stored!)!.Label.Should().Be("default");
    }

    [Test]
    public async Task A_train_taking_Unit_can_be_queued_without_input()
    {
        var act = async () => await Execution.QueueAsync(typeof(IUnitInputTrain).FullName!, null);

        await act.Should()
            .NotThrowAsync("Unit needs no values, so a missing input stands in for it");
    }

    [Test]
    public async Task An_explicit_empty_object_is_read_as_given_and_not_as_a_missing_input()
    {
        // Only a missing input is held to the stricter reading. An explicit {} is the caller's
        // own input, and is deserialized the way any other input is.
        var result = await Execution.QueueAsync(typeof(IPositionalInputTrain).FullName!, "{}");

        (await EntryAsync(result.WorkQueueId)).Input.Should().NotBeNull();
    }

    [Test]
    public async Task RunAsync_reads_a_missing_input_the_way_QueueAsync_does()
    {
        var refused = async () =>
            await Execution.RunAsync(typeof(IPositionalInputTrain).FullName!, "");
        var accepted = async () => await Execution.RunAsync(typeof(IUnitInputTrain).FullName!, " ");

        (await refused.Should().ThrowAsync<JsonException>()).WithMessage("No input was given*");
        await accepted.Should().NotThrowAsync();
    }

    private async Task<int> WorkQueueCountAsync()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        return await context.WorkQueues.CountAsync();
    }

    [Test]
    public async Task A_hook_receives_the_input_even_when_none_was_given()
    {
        HookProbe.Seen.Clear();

        await Execution.QueueAsync(typeof(IHookedOptionalInputTrain).FullName!, "");

        HookProbe.Seen.Should().ContainSingle().Which.Should().NotBeNull();
    }

    [Test]
    public async Task Whitespace_input_is_read_as_an_empty_object()
    {
        var result = await Execution.QueueAsync(typeof(IOptionalInputTrain).FullName!, " \t\r\n ");

        var stored = (await EntryAsync(result.WorkQueueId)).Input;

        stored.Should().NotBeNull("blank input is documented to be read as {}, the same as null");
        JsonSerializer.Deserialize<OptionalInput>(stored!)!.Label.Should().Be("default");
    }

    [Test]
    public async Task A_scheduled_time_in_the_past_is_stored_as_given_and_is_already_due()
    {
        var past = DateTime.UtcNow.AddHours(-3);

        var result = await Execution.QueueAsync(
            typeof(IOptionalInputTrain).FullName!,
            "{}",
            scheduledAt: past
        );

        var entry = await EntryAsync(result.WorkQueueId);

        entry
            .ScheduledAt.Should()
            .BeCloseTo(past, TimeSpan.FromSeconds(1), "the enqueue neither refuses nor clamps it");
        entry
            .ScheduledAt.Should()
            .BeOnOrBefore(
                DateTime.UtcNow,
                "it is the earliest dispatch time, so a past one is due as soon as a worker is free"
            );
        entry.ConfirmedAt.Should().NotBeNull("nothing stages it, so dispatch can claim it now");
    }

    [Test]
    public async Task A_local_scheduled_time_is_stored_as_utc()
    {
        // The conversion only shows on a machine whose zone is not UTC, and CI runs in UTC, where
        // converting and relabelling store the same instant. So the test picks the zone: TZ is
        // read when the local zone is next computed. Nothing in this assembly runs in parallel.
        var previousZone = Environment.GetEnvironmentVariable("TZ");
        Environment.SetEnvironmentVariable("TZ", "Asia/Kolkata");
        TimeZoneInfo.ClearCachedData();
        try
        {
            var offset = TimeSpan.FromHours(5.5);
            if (TimeZoneInfo.Local.BaseUtcOffset != offset)
                Assert.Ignore("this platform does not take its local zone from TZ");

            var instant = new DateTime(2031, 5, 1, 12, 0, 0, DateTimeKind.Utc);
            var local = DateTime.SpecifyKind(instant + offset, DateTimeKind.Local);

            var result = await Execution.QueueAsync(
                typeof(IOptionalInputTrain).FullName!,
                "{}",
                scheduledAt: local
            );

            (await EntryAsync(result.WorkQueueId))
                .ScheduledAt.Should()
                .Be(
                    instant,
                    "17:30 in UTC+05:30 is 12:00 UTC; relabelling it as UTC would schedule it "
                        + "five and a half hours late"
                );
        }
        finally
        {
            Environment.SetEnvironmentVariable("TZ", previousZone);
            TimeZoneInfo.ClearCachedData();
        }
    }

    [Test]
    public async Task An_unspecified_scheduled_time_is_taken_as_utc()
    {
        var unspecified = DateTime.SpecifyKind(
            DateTime.UtcNow.AddHours(2),
            DateTimeKind.Unspecified
        );

        var result = await Execution.QueueAsync(
            typeof(IOptionalInputTrain).FullName!,
            "{}",
            scheduledAt: unspecified
        );

        (await EntryAsync(result.WorkQueueId))
            .ScheduledAt.Should()
            .BeCloseTo(
                DateTime.SpecifyKind(unspecified, DateTimeKind.Utc),
                TimeSpan.FromSeconds(1),
                "a timestamp without an offset arrives from JSON as unspecified"
            );
    }

    [Test]
    public async Task Concurrent_enqueues_on_one_scope_do_not_interfere()
    {
        const int enqueues = 5;
        ConcurrentHookProbe.Reset(enqueues);
        var execution = Execution;

        var all = Task.WhenAll(
            Enumerable
                .Range(0, enqueues)
                .Select(_ =>
                    Task.Run(() =>
                        execution.QueueAsync(typeof(IConcurrentHookTrain).FullName!, "{}")
                    )
                )
        );

        var act = async () => await all.WaitAsync(TimeSpan.FromSeconds(30));
        await act.Should()
            .NotThrowAsync("a Blazor circuit shares one scope across every enqueue it makes");

        // Every hook waited for the others before reading the context again, so all five were in
        // flight at once. Each has to have seen a context of its own, and kept it throughout.
        // Asserted on projections: the contexts are disposed by now and cannot be formatted.
        ConcurrentHookProbe.Seen.Should().HaveCount(enqueues);
        ConcurrentHookProbe
            .Seen.Count(seen => seen.Before is not null && ReferenceEquals(seen.Before, seen.After))
            .Should()
            .Be(
                enqueues,
                "another enqueue's hook must not replace the context this one is writing through"
            );
        ConcurrentHookProbe
            .Seen.Select(seen => seen.Before)
            .Distinct(ReferenceEqualityComparer.Instance)
            .Count()
            .Should()
            .Be(enqueues, "each enqueue commits on its own context");

        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        (await context.WorkQueues.CountAsync(w => w.TrainName!.Contains("ConcurrentHookTrain")))
            .Should()
            .Be(enqueues, "every enqueue committed its own row");
    }

    [Test]
    public async Task A_hook_can_enqueue_another_train()
    {
        var result = await Execution.QueueAsync(typeof(IChainingHookTrain).FullName!, "{}");

        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        (await context.WorkQueues.CountAsync(w => w.TrainName!.Contains("OptionalInputTrain")))
            .Should()
            .Be(1, "the hook's own enqueue gets its own context rather than refusing to nest");
        (await EntryAsync(result.WorkQueueId)).Should().NotBeNull();
    }

    // ── probes ──────────────────────────────────────────────────────

    public static class HookProbe
    {
        public static readonly System.Collections.Concurrent.ConcurrentBag<object?> Seen = [];
    }

    /// <summary>
    /// Holds each concurrent hook until all of them are running, and records the ambient context
    /// each one read before and after that wait.
    /// </summary>
    public static class ConcurrentHookProbe
    {
        private static int _expected;
        private static int _arrived;
        private static TaskCompletionSource _allArrived = new();

        public static System.Collections.Concurrent.ConcurrentBag<(
            object? Before,
            object? After
        )> Seen { get; private set; } = [];

        public static void Reset(int expected)
        {
            _expected = expected;
            _arrived = 0;
            _allArrived = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            Seen = [];
        }

        public static Task ArriveAsync(CancellationToken ct)
        {
            if (Interlocked.Increment(ref _arrived) == _expected)
                _allArrived.TrySetResult();

            return _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        }
    }

    public record ConcurrentHookInput
    {
        public string Label { get; init; } = "default";
    }

    public interface IConcurrentHookTrain : IServiceTrain<ConcurrentHookInput, Unit>;

    public class ConcurrentHookTrain(IEnqueueContextAccessor accessor)
        : ServiceTrain<ConcurrentHookInput, Unit>,
            IConcurrentHookTrain
    {
        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            var before = accessor.Current;
            await ConcurrentHookProbe.ArriveAsync(ct);
            ConcurrentHookProbe.Seen.Add((before, accessor.Current));
        }

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public record OptionalInput
    {
        public string Label { get; init; } = "default";
    }

    public record HookedOptionalInput
    {
        public string Label { get; init; } = "default";
    }

    public record RequiredInput
    {
        public required string Label { get; init; }
    }

    public record ChainingInput
    {
        public string Label { get; init; } = "default";
    }

    public interface IOptionalInputTrain : IServiceTrain<OptionalInput, Unit>;

    public class OptionalInputTrain : ServiceTrain<OptionalInput, Unit>, IOptionalInputTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public interface IHookedOptionalInputTrain : IServiceTrain<HookedOptionalInput, Unit>;

    public class HookedOptionalInputTrain
        : ServiceTrain<HookedOptionalInput, Unit>,
            IHookedOptionalInputTrain
    {
        protected override Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            HookProbe.Seen.Add(metadata.GetInput<HookedOptionalInput>());
            return Task.CompletedTask;
        }

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public record PositionalInput(string Id, string NewName);

    public record DefaultedPositionalInput(string Label = "default");

    public interface IPositionalInputTrain : IServiceTrain<PositionalInput, Unit>;

    public class PositionalInputTrain : ServiceTrain<PositionalInput, Unit>, IPositionalInputTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public interface IDefaultedPositionalInputTrain : IServiceTrain<DefaultedPositionalInput, Unit>;

    public class DefaultedPositionalInputTrain
        : ServiceTrain<DefaultedPositionalInput, Unit>,
            IDefaultedPositionalInputTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public interface IUnitInputTrain : IServiceTrain<Unit, Unit>;

    public class UnitInputTrain : ServiceTrain<Unit, Unit>, IUnitInputTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public interface IRequiredInputTrain : IServiceTrain<RequiredInput, Unit>;

    public class RequiredInputTrain : ServiceTrain<RequiredInput, Unit>, IRequiredInputTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public interface IChainingHookTrain : IServiceTrain<ChainingInput, Unit>;

    public class ChainingHookTrain(ITrainExecutionService execution)
        : ServiceTrain<ChainingInput, Unit>,
            IChainingHookTrain
    {
        protected override Task OnQueue(Metadata metadata, CancellationToken ct) =>
            execution.QueueAsync(typeof(IOptionalInputTrain).FullName!, "{}", ct: ct);

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }
}
