using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// A client may send the result its twin predicted as <c>clientResult</c>. A mismatch is refused as
/// <c>CLIENT_DIVERGENCE</c>, writes nothing and is counted on the twin-drift meter; a replayed request is compared with
/// the result recorded for it, never with the row as it is now; and the value is capped and logged only as a hash and
/// a length.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
[NonParallelizable]
public class MachineClientResultE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Database = "trax_sm_machine_client_result";

    private const string ClientDivergence = "CLIENT_DIVERGENCE";

    private IHost _host = null!;
    private readonly CapturingLoggerProvider _logs = new();

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            E2EHostOptions.Wizard() with
            {
                Services = s => s.AddSingleton<ILoggerProvider>(_logs),
            }
        );
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        _host.Dispose();
        await E2EHost.DropDatabaseAsync(Database);
    }

    // A snapshot no twin of the wizard could predict for "back" from Paying.
    private static string WrongPrediction(Guid id, string marker = "nobody") =>
        new JsonObject
        {
            ["machine"] = WizardMachine.MachineId,
            ["version"] = 1,
            ["state"] = nameof(WizardState.Built),
            ["context"] = new JsonObject { ["title"] = marker, ["artifact"] = id.ToString() },
        }.ToJsonString();

    // What a twin predicts for "back" from Paying: the same context, in Naming.
    private static string RightPrediction(MachineResult paying) =>
        new JsonObject
        {
            ["machine"] = WizardMachine.MachineId,
            ["version"] = 1,
            ["state"] = nameof(WizardState.Naming),
            ["context"] = JsonNode.Parse(paying.Context.GetRawText()),
        }.ToJsonString();

    [Test]
    public async Task A_mismatched_clientResult_is_refused_as_client_divergence_and_counted()
    {
        var paying = await _host.WizardInPayingAsync(E2EHost.AliceKey);
        var before = await E2ESql.DraftAsync(Database, WizardMachine.MachineId, paying.Id);
        using var meter = new DivergenceMeter();

        using var doc = await _host.PostAsync(
            WizardOps.Event(
                "back",
                paying.Id,
                WizardOps.NewRequestId(),
                clientResult: WrongPrediction(paying.Id)
            ),
            apiKey: E2EHost.AliceKey
        );

        WizardOps
            .Result(doc, "back")
            .ProblemCode.Should()
            .Be(ClientDivergence, "a mismatched prediction is refused, per " + Adr);
        (await E2ESql.DraftAsync(Database, WizardMachine.MachineId, paying.Id))
            .Should()
            .Be(before, "a refused divergence writes nothing: the revision is unchanged");
        meter.Count.Should().Be(1, "each divergence is counted on the twin-drift meter");
    }

    [Test]
    public async Task A_replayed_request_does_not_falsely_diverge()
    {
        var paying = await _host.WizardInPayingAsync(E2EHost.AliceKey);
        var requestId = WizardOps.NewRequestId();
        var prediction = RightPrediction(paying);
        var first = await _host.SendWizardEventAsync(
            E2EHost.AliceKey,
            "back",
            paying.Id,
            requestId: requestId,
            clientResult: prediction
        );
        first.ProblemCode.Should().BeNull(first.Raw.GetRawText());

        // A write in between: the row moves on past the recorded result.
        await _host.AdvanceWizardAsync(
            E2EHost.AliceKey,
            "describe",
            paying.Id,
            WizardOps.DetailsInput(title: "Moved on")
        );
        using var meter = new DivergenceMeter();

        var replayed = await _host.SendWizardEventAsync(
            E2EHost.AliceKey,
            "back",
            paying.Id,
            requestId: requestId,
            clientResult: prediction
        );

        replayed
            .ProblemCode.Should()
            .NotBe(
                ClientDivergence,
                "a replay is compared with the result recorded for its request, not the row as it is now, "
                    + "per "
                    + Adr
            );
        replayed.Raw.GetRawText().Should().Be(first.Raw.GetRawText(), "the ring's result");
        meter.Count.Should().Be(0, "a replay is no divergence");
    }

    [Test]
    public async Task ClientResult_is_capped_and_only_its_hash_and_length_are_logged()
    {
        var paying = await _host.WizardInPayingAsync(E2EHost.AliceKey);
        var marker = $"marker-{Guid.NewGuid():N}";
        var wrong = WrongPrediction(paying.Id, marker);
        _logs.Clear();

        var diverged = await _host.SendWizardEventAsync(
            E2EHost.AliceKey,
            "back",
            paying.Id,
            clientResult: wrong
        );
        diverged.ProblemCode.Should().Be(ClientDivergence);
        _logs
            .Messages.Should()
            .NotContain(
                m => m.Contains(marker, StringComparison.Ordinal),
                "only a hash and a length of clientResult are logged, never its content, per " + Adr
            );
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(wrong)));
        _logs
            .Messages.Should()
            .Contain(
                m =>
                    m.Contains($"{wrong.Length} characters", StringComparison.Ordinal)
                    && m.Contains($"SHA-256 {hash}", StringComparison.Ordinal),
                "the divergence is logged with the value's length and its SHA-256 hash"
            );

        using var meter = new DivergenceMeter();
        var oversized = await _host.SendWizardEventAsync(
            E2EHost.AliceKey,
            "back",
            paying.Id,
            clientResult: new string('x', 4 * 1024 * 1024)
        );
        oversized
            .ProblemCode.Should()
            .Be(
                RefusalCode.TooLarge.ToWire(),
                "an oversized clientResult is refused before comparing"
            );
        meter.Count.Should().Be(0, "it never reached the comparison");
    }

    /// <summary>Counts the twin-drift instrument's measurements while it lives.</summary>
    private sealed class DivergenceMeter : IDisposable
    {
        private readonly MeterListener _listener = new();
        private long _count;

        public DivergenceMeter()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (
                    instrument.Meter.Name == StateMachineInstruments.MeterName
                    && instrument.Name == StateMachineInstruments.ClientDivergence
                )
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>(
                (_, value, _, _) => Interlocked.Add(ref _count, value)
            );
            _listener.SetMeasurementEventCallback<int>(
                (_, value, _, _) => Interlocked.Add(ref _count, value)
            );
            _listener.Start();
        }

        public long Count => Interlocked.Read(ref _count);

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>Keeps every formatted log message of the host.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages.ToArray();

        public void Clear() => _messages.Clear();

        public ILogger CreateLogger(string categoryName) => new Logger(_messages);

        public void Dispose() { }

        private sealed class Logger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            ) => messages.Enqueue(formatter(state, exception) + " " + exception);
        }
    }
}
