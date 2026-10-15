using AwesomeAssertions;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// Every typed event, <c>start</c> and <c>discard</c> carries a client request id, and a retry with the same id is
/// answered with the recorded result instead of running again: twenty concurrent retries of the effect-bound event
/// run its effect once, a retry after a server write in between replays from the row's ring, twenty concurrent
/// starts create one instance, and an id reused for a different request is <c>REQUEST_ID_REUSED</c>. The nodes are
/// two, so a write the client did not make comes from the other node.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
[NonParallelizable]
public class MachineRequestIdE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Database = "trax_sm_machine_request_id";

    private TwoNodes _nodes = null!;
    private CountingCharge _charge = null!;

    private IHost A => _nodes.A;

    [OneTimeSetUp]
    public async Task Up()
    {
        _charge = new CountingCharge();
        _nodes = await TwoNodes.StartAsync(
            Database,
            _charge,
            new E2EHostOptions
            {
                // The outcome of a run ended on the database reaches the instance within a sweep.
                Served = [typeof(WizardMachine)],
                Machines = o =>
                {
                    o.InvokeOutcomeSweepInterval = TimeSpan.FromMilliseconds(250);
                },
            }
        );
    }

    [OneTimeTearDown]
    public async Task Down() => await _nodes.DisposeAsync();

    [Test]
    public async Task RequestId_is_required_on_every_event_start_and_discard()
    {
        var executor = await A.ExecutorAsync();
        var wizard = SchemaWalk.MachineType(
            executor,
            SchemaWalk.RootMutation,
            WizardMachine.MachineId
        );
        wizard.Should().NotBeNull("the wizard is exposed, per " + Adr);
        foreach (var field in wizard!.Fields.Where(f => !f.IsIntrospectionField))
            field
                .Arguments.TryGetField("requestId", out var requestId)
                .Should()
                .BeTrue($"{field.Name} takes a request id, per {Adr}");
        wizard
            .Fields.Where(f => !f.IsIntrospectionField)
            .Select(f => f.Arguments["requestId"].Type.Print())
            .Should()
            .AllBe("String!", "the request id is required, never optional");

        var paying = await A.WizardInPayingAsync(E2EHost.AliceKey);
        var before = await E2ESql.DraftAsync(Database, WizardMachine.MachineId, paying.Id);
        foreach (var requestId in new[] { "", new string('r', 10_000) })
        {
            using var doc = await A.PostAsync(
                WizardOps.Event("back", paying.Id, requestId),
                apiKey: E2EHost.AliceKey
            );
            (
                WizardOps.ErrorCodes(doc).Count > 0
                || WizardOps.Result(doc, "back").ProblemCode is not null
            )
                .Should()
                .BeTrue(
                    $"an empty or oversized request id ({requestId.Length} characters) is refused: "
                        + doc.RootElement.GetRawText()[
                            ..Math.Min(500, doc.RootElement.GetRawText().Length)
                        ]
                );
        }
        (await E2ESql.DraftAsync(Database, WizardMachine.MachineId, paying.Id))
            .Should()
            .Be(before, "a refused request id writes nothing");
    }

    [Test]
    public async Task A_request_id_with_a_NUL_is_refused_on_every_operation_before_anything_runs()
    {
        const string nul = "a\0b";
        var paying = await A.WizardInPayingAsync(E2EHost.AliceKey);
        var before = await E2ESql.DraftAsync(Database, WizardMachine.MachineId, paying.Id);
        var drafts = await E2ESql.CountDraftsAsync(Database, WizardMachine.MachineId);
        var callsBefore = _charge.Calls;

        foreach (
            var (operation, field) in new[]
            {
                (WizardOps.Start("Atlas", nul), "start"),
                (WizardOps.Event("back", paying.Id, nul), "back"),
                (WizardOps.Event("pay", paying.Id, nul), "pay"),
                (WizardOps.Discard(paying.Id, nul), "discard"),
            }
        )
        {
            using var doc = await A.PostAsync(operation, apiKey: E2EHost.AliceKey);
            WizardOps
                .Result(doc, field)
                .ProblemCode.Should()
                .Be(
                    RefusalCode.InvalidRequestId.ToWire(),
                    $"{field} with a NUL in its request id is refused as data, never a server error. See {Adr}"
                );
        }

        _charge.Calls.Should().Be(callsBefore, "a refused send delivers nothing");
        (await E2ESql.DraftAsync(Database, WizardMachine.MachineId, paying.Id))
            .Should()
            .Be(before, "a refused request id writes nothing");
        (await E2ESql.CountDraftsAsync(Database, WizardMachine.MachineId))
            .Should()
            .Be(drafts, "a refused start creates nothing");
    }

    [Test]
    public async Task An_effect_bound_event_runs_its_effect_once_under_retries_with_one_requestId()
    {
        var paying = await A.WizardInPayingAsync(E2EHost.AliceKey);
        var requestId = WizardOps.NewRequestId();
        var callsBefore = _charge.Calls;

        var answers = await Task.WhenAll(
            Enumerable
                .Range(0, 20)
                .Select(_ =>
                    A.SendWizardEventAsync(E2EHost.AliceKey, "pay", paying.Id, requestId: requestId)
                )
        );

        (_charge.Calls - callsBefore)
            .Should()
            .Be(1, "twenty retries of one request run the irreversible effect once, per " + Adr);
        answers
            .Select(a => a.Raw.GetRawText())
            .Distinct()
            .Should()
            .ContainSingle("every retry is answered with the one recorded snapshot");
        answers[0].StateIs(WizardState.Approving).Should().BeTrue(answers[0].Raw.GetRawText());
    }

    [Test]
    public async Task The_entering_event_queues_one_run_under_retries()
    {
        var approving = await A.WizardInApprovingAsync(E2EHost.AliceKey);
        var requestId = WizardOps.NewRequestId();

        var answers = await Task.WhenAll(
            Enumerable
                .Range(0, 20)
                .Select(i =>
                    (i % 2 == 0 ? A : _nodes.B).SendWizardEventAsync(
                        E2EHost.AliceKey,
                        "approve",
                        approving.Id,
                        requestId: requestId
                    )
                )
        );

        (await E2ESql.CountInvokedRunsAsync(Database, approving.Id))
            .Should()
            .Be(
                1,
                "twenty retries of the event that enters the invoking state queue its train once, per "
                    + Adr
            );
        answers
            .Select(a => a.Raw.GetRawText())
            .Distinct()
            .Should()
            .ContainSingle("every retry is answered with the one recorded snapshot");
        answers[0].StateIs(WizardState.Building).Should().BeTrue(answers[0].Raw.GetRawText());
    }

    [Test]
    public async Task One_requestId_on_two_different_events_is_request_id_reused()
    {
        var paying = await A.WizardInPayingAsync(E2EHost.AliceKey);
        var requestId = WizardOps.NewRequestId();

        var back = await A.AdvanceWizardAsync(
            E2EHost.AliceKey,
            "back",
            paying.Id,
            requestId: requestId
        );
        back.StateIs(WizardState.Naming).Should().BeTrue();
        var before = await E2ESql.DraftAsync(Database, WizardMachine.MachineId, paying.Id);

        var reused = await A.SendWizardEventAsync(
            E2EHost.AliceKey,
            "describe",
            paying.Id,
            WizardOps.DetailsInput(),
            requestId
        );

        reused
            .ProblemCode.Should()
            .Be(
                RefusalCode.RequestIdReused.ToWire(),
                "a request id names one request; sending it with another event is refused, per "
                    + Adr
            );
        (await E2ESql.DraftAsync(Database, WizardMachine.MachineId, paying.Id)).Should().Be(before);
    }

    [Test]
    public async Task An_event_retried_after_a_server_write_replays_from_the_ring()
    {
        var approving = await A.WizardInApprovingAsync(E2EHost.AliceKey);
        var requestId = WizardOps.NewRequestId();
        var approved = await A.AdvanceWizardAsync(
            E2EHost.AliceKey,
            "approve",
            approving.Id,
            requestId: requestId
        );
        approved.StateIs(WizardState.Building).Should().BeTrue(approved.Raw.GetRawText());
        (await E2ESql.CountInvokedRunsAsync(Database, approving.Id)).Should().Be(1);

        // The server writes the row from the other node: the build's run fails, and its outcome moves the draft.
        await FailQueuedRunsAsync(_nodes.B, approving.Id);
        (
            await E2EWait.UntilAsync(async () =>
                (await E2ESql.DraftAsync(Database, WizardMachine.MachineId, approving.Id))?.State
                == nameof(WizardState.BuildFailed)
            )
        )
            .Should()
            .BeTrue("the failed run's outcome is applied to the draft");
        var afterOutcome = await E2ESql.DraftAsync(Database, WizardMachine.MachineId, approving.Id);

        var retried = await A.SendWizardEventAsync(
            E2EHost.AliceKey,
            "approve",
            approving.Id,
            requestId: requestId
        );

        retried
            .Raw.GetRawText()
            .Should()
            .Be(
                approved.Raw.GetRawText(),
                "a retry is answered from the ring with the result recorded for its request id, however "
                    + "the row moved since, per "
                    + Adr
            );
        (await E2ESql.CountInvokedRunsAsync(Database, approving.Id))
            .Should()
            .Be(1, "nothing fired again");
        (await E2ESql.DraftAsync(Database, WizardMachine.MachineId, approving.Id))
            .Should()
            .Be(afterOutcome);
    }

    [Test]
    public async Task The_owner_reads_the_run_the_invoking_state_queued_and_nobody_else_does()
    {
        var approving = await A.WizardInApprovingAsync(E2EHost.AliceKey);
        await A.AdvanceWizardAsync(E2EHost.AliceKey, "approve", approving.Id);
        var token = await E2ESql.ScalarAsync<string>(
            Database,
            "SELECT invoke_token FROM trax.snapshot_draft WHERE machine = @m AND id = @id",
            ("m", WizardMachine.MachineId),
            ("id", approving.Id)
        );
        token.Should().NotBeNull("Building queued a run");

        (await InvokedRunAsync(E2EHost.AliceKey, approving.Id))
            .Should()
            .Be(token, "the owner follows the run its state queued through runProgress");
        (await InvokedRunAsync(E2EHost.BobKey, approving.Id))
            .Should()
            .BeNull("another caller reads no instance, and no run of it");

        await FailQueuedRunsAsync(_nodes.B, approving.Id);
        (
            await E2EWait.UntilAsync(async () =>
                (await E2ESql.DraftAsync(Database, WizardMachine.MachineId, approving.Id))?.State
                == nameof(WizardState.BuildFailed)
            )
        )
            .Should()
            .BeTrue();
        (await InvokedRunAsync(E2EHost.AliceKey, approving.Id))
            .Should()
            .BeNull("a state the outcome moved the instance to queued nothing");
    }

    private async Task<string?> InvokedRunAsync(string apiKey, Guid id)
    {
        using var doc = await A.PostAsync(
            $$"""{ machines { wizard { get(id: "{{id}}") { state invokedRun { runId } } } } }""",
            apiKey: apiKey
        );
        doc.RootElement.TryGetProperty("errors", out var errors)
            .Should()
            .BeFalse(errors.ToString());
        var get = doc
            .RootElement.GetProperty("data")
            .GetProperty("machines")
            .GetProperty("wizard")
            .GetProperty("get");
        return
            get.ValueKind == System.Text.Json.JsonValueKind.Object
            && get.GetProperty("invokedRun")
                is { ValueKind: System.Text.Json.JsonValueKind.Object } run
            ? run.GetProperty("runId").GetString()
            : null;
    }

    [Test]
    public async Task Twenty_concurrent_starts_with_one_requestId_create_one_instance()
    {
        var requestId = WizardOps.NewRequestId();
        var draftsBefore = await E2ESql.CountDraftsAsync(Database, WizardMachine.MachineId);

        var answers = await Task.WhenAll(
            Enumerable
                .Range(0, 20)
                .Select(i =>
                    (i % 2 == 0 ? A : _nodes.B).StartWizardAsync(
                        E2EHost.AliceKey,
                        title: "Atlas",
                        requestId: requestId
                    )
                )
        );

        answers
            .Select(a => a.Id)
            .Distinct()
            .Should()
            .ContainSingle("one start request creates one instance, on whichever node, per " + Adr);
        (await E2ESql.CountDraftsAsync(Database, WizardMachine.MachineId))
            .Should()
            .Be(draftsBefore + 1);
    }

    [Test]
    public async Task A_start_retry_with_a_different_input_is_request_id_reused()
    {
        var requestId = WizardOps.NewRequestId();
        await A.StartWizardAsync(E2EHost.AliceKey, title: "Atlas", requestId: requestId);
        var draftsBefore = await E2ESql.CountDraftsAsync(Database, WizardMachine.MachineId);

        using var doc = await A.PostAsync(
            WizardOps.Start("Borealis", requestId),
            apiKey: E2EHost.AliceKey
        );

        WizardOps
            .Result(doc, "start")
            .ProblemCode.Should()
            .Be(
                RefusalCode.RequestIdReused.ToWire(),
                "a start retried with another input is a different request under the same id, per "
                    + Adr
            );
        (await E2ESql.CountDraftsAsync(Database, WizardMachine.MachineId))
            .Should()
            .Be(draftsBefore);
    }

    // What the dispatcher and a failing run do, from the node given: each queued run of the instance is claimed,
    // recorded as its run, and ended Failed, so the outcome reconciler applies the failed outcome.
    private static async Task FailQueuedRunsAsync(IHost node, Guid instance)
    {
        using var scope = node.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDataContext>();
        var queued = await db
            .WorkQueues.AsNoTracking()
            .Where(w => w.InvokingInstanceId == instance && w.Status == WorkQueueStatus.Queued)
            .ToListAsync();
        foreach (var entry in queued)
        {
            var run = Metadata.Create(
                new CreateMetadata
                {
                    Name = entry.TrainName,
                    ExternalId = entry.ExternalId,
                    Input = null,
                    InvokedBy = new InvokedBy(
                        entry.InvokingMachine!,
                        instance,
                        entry.InvokingOwnerKind!.Value
                    ),
                }
            );
            run.TrainState = TrainState.InProgress;
            await db.Track(run);
            await db.SaveChanges(CancellationToken.None);
            await db
                .WorkQueues.Where(w => w.Id == entry.Id)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(w => w.Status, WorkQueueStatus.Dispatched)
                        .SetProperty(w => w.MetadataId, run.Id)
                );
        }
        await db
            .Metadatas.Where(m => m.InvokingInstanceId == instance)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.TrainState, TrainState.Failed));
    }
}
