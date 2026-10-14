using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate.Types;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;
using Trax.Api.Auth.Jwt.Testing;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// <c>machineChanged</c> over a real socket, on two nodes over one database: the subscriber registers before the
/// first read, whose snapshot is the first event; every write after it arrives as a whole snapshot with a rising
/// revision, a burst coalesced to its latest, an outcome applied on the other node included; a delete or an expiry
/// ends the stream with <c>gone</c>; a stream follows one incarnation of a row; a missing id, another user's and a
/// system instance's get one answer; the read rule is checked once and the user key is the socket's principal; a
/// stream ends when its token expires or a newer version writes its row; subscriptions are capped and count toward
/// the socket's cap; and a notice that changes nothing sends nothing, costs one coalesced read, and an unreadable row
/// ends only its own stream.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
[NonParallelizable]
public class MachineChangedE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Database = "trax_sm_machine_changed";

    /// <summary>The database of the tests that run two versions of a machine.</summary>
    private const string VersionsDatabase = "trax_sm_machine_changed_versions";

    /// <summary>The code an operation past the socket's limit is refused with (api/0015).</summary>
    private const string SocketLimitCode = "TRAX_SOCKET_OPERATION_LIMIT";

    /// <summary>The change feed's channel; a notice carries only the row id.</summary>
    private const string Channel = "trax_snapshot_changed";

    private const string JwtIssuer = "https://sm-e2e-issuer";
    private const string JwtAudience = "sm-e2e";
    private static readonly byte[] JwtKey = Encoding.UTF8.GetBytes(new string('j', 32));
    private static readonly TestTokenIssuer Tokens = TestTokenIssuer.Symmetric(
        JwtIssuer,
        JwtAudience,
        JwtKey
    );

    private TwoNodes _nodes = null!;

    private IHost A => _nodes.A;

    private IHost B => _nodes.B;

    [OneTimeSetUp]
    public async Task Up()
    {
        _nodes = await TwoNodes.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions
            {
                Served = [typeof(WizardMachine)],
                Machines = o =>
                {
                    // The outcome of a run ended on the database reaches the instance within a sweep.
                    o.InvokeOutcomeSweepInterval = TimeSpan.FromMilliseconds(250);
                    // An explicit draft lifetime, so a draft held past it is expired by the sweeper.
                    o.DraftTtl = TimeSpan.FromHours(1);
                },
                GraphQL = MachineDocumentOps.Persisted(),
                Authentication = services =>
                    services.AddTraxJwtAuth(jwt =>
                        jwt.UseSymmetricKey(JwtIssuer, JwtAudience, JwtKey)
                    ),
                Services = services =>
                {
                    CountingDataContexts.Install(services);
                    PrincipalReads.Install(services);
                },
            }
        );
    }

    [OneTimeTearDown]
    public async Task Down() => await _nodes.DisposeAsync();

    // Each test starts from an empty table, so the instances one test leaves behind never count against the next.
    [SetUp]
    public async Task Clear() =>
        await E2ESql.ExecuteAsync(Database, "DELETE FROM trax.snapshot_draft");

    #region Registration

    [Test]
    public async Task The_machineChanged_document_is_registered_under_the_reserved_prefix()
    {
        (await MachineDocumentOps.MachineDocumentsAsync(Database, WizardMachine.MachineId))
            .Where(d => d.Operation(WizardMachine.MachineId) == MachineDocumentOps.MachineChanged)
            .Should()
            .ContainSingle("the subscription is a persisted document like the others, per " + Adr)
            .Which.IsActive.Should()
            .BeTrue();
    }

    [Test]
    public async Task It_runs_by_id_over_the_socket()
    {
        var started = await A.StartWizardAsync(E2EHost.AliceKey);
        var documentId = await MachineDocumentOps.IdOfAsync(
            Database,
            WizardMachine.MachineId,
            MachineDocumentOps.MachineChanged
        );
        await using var ws = await GraphQLWs.ConnectAsync(A, E2EHost.AliceKey);

        await ws.SubscribePersistedAsync("1", documentId, new { id = started.Id });
        var first = await ws.NextChangeAsync("1");

        first
            .Revision.Should()
            .Be(started.Revision!.Value, "the first event is the current snapshot");
        first.StateIs(nameof(WizardState.Naming)).Should().BeTrue(first.Raw.GetRawText());
    }

    #endregion

    #region Ordering

    [Test]
    public async Task The_subscriber_registers_before_the_initial_read_and_the_first_event_is_the_current_snapshot()
    {
        var started = await A.StartWizardAsync(E2EHost.AliceKey);
        await using var ws = await GraphQLWs.ConnectAsync(A, E2EHost.AliceKey);

        // Writes from the other node race the subscription's opening read; none may fall between its registration
        // and that read.
        await ws.SubscribeAsync("1", MachineChangedOps.Subscribe(started.Id));
        var final = await WriteManyAsync(B, started.Id, 10);
        var events = await ws.ReadUntilRevisionAsync("1", final);

        events[0]
            .Revision.Should()
            .BeGreaterThanOrEqualTo(
                started.Revision!.Value,
                "the first event is a current snapshot"
            );
        events[^1]
            .Revision.Should()
            .Be(final, "nothing written while the stream opened is lost, per " + Adr);
        events.Select(e => e.Revision).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Test]
    public async Task Revisions_arrive_in_order_never_repeating_or_decreasing()
    {
        var started = await A.StartWizardAsync(E2EHost.AliceKey);
        await using var ws = await GraphQLWs.ConnectAsync(A, E2EHost.AliceKey);
        await ws.SubscribeAsync("1", MachineChangedOps.Subscribe(started.Id));
        var first = await ws.NextChangeAsync("1");

        var final = await WriteManyAsync(A, started.Id, 10);
        var events = await ws.ReadUntilRevisionAsync("1", final);

        events
            .Select(e => e.Revision)
            .Prepend(first.Revision)
            .Should()
            .BeInAscendingOrder("revisions only rise, per " + Adr)
            .And.OnlyHaveUniqueItems("no revision is sent twice");
        events[^1].Revision.Should().Be(final);
    }

    [Test]
    public async Task A_burst_is_coalesced_to_the_latest_revision_and_a_skipped_revision_triggers_no_reload()
    {
        var started = await A.StartWizardAsync(E2EHost.AliceKey);
        await using var ws = await GraphQLWs.ConnectAsync(A, E2EHost.AliceKey);
        await ws.SubscribeAsync("1", MachineChangedOps.Subscribe(started.Id));
        var first = await ws.NextChangeAsync("1");

        var final = await WriteManyAsync(B, started.Id, 30);
        var events = await ws.ReadUntilRevisionAsync("1", final);

        events[^1].Revision.Should().Be(final, "the burst ends on its newest revision, per " + Adr);
        events
            .Select(e => e.Revision)
            .Prepend(first.Revision)
            .Should()
            .BeInAscendingOrder()
            .And.OnlyHaveUniqueItems();
        events
            .Should()
            .NotContain(
                e => e.Reload,
                "each event is a whole snapshot, so a skipped revision is harmless and asks for no reload"
            );
    }

    [Test]
    public async Task A_server_made_outcome_applied_on_another_host_reaches_the_subscriber()
    {
        var approving = await A.WizardInApprovingAsync(E2EHost.AliceKey);
        await using var ws = await GraphQLWs.ConnectAsync(A, E2EHost.AliceKey);
        await ws.SubscribeAsync("1", MachineChangedOps.Subscribe(approving.Id));
        await ws.NextChangeAsync("1");
        await A.AdvanceWizardAsync(E2EHost.AliceKey, "approve", approving.Id);

        // The build's run fails on the database, from the other node; whichever node applies the outcome, the
        // subscriber on this one hears of it.
        await FailQueuedRunsAsync(B, approving.Id);

        MachineChange change;
        do change = await ws.NextChangeAsync("1", TimeSpan.FromSeconds(20));
        while (!change.StateIs(nameof(WizardState.BuildFailed)));
        change.StateIs(nameof(WizardState.BuildFailed)).Should().BeTrue("per " + Adr);
    }

    #endregion

    #region Who may watch

    [Test]
    public async Task A_missing_id_another_users_id_and_a_system_instance_id_get_the_same_answer()
    {
        var alices = await A.StartWizardAsync(E2EHost.AliceKey);
        await using (var own = await GraphQLWs.ConnectAsync(A, E2EHost.AliceKey))
        {
            await own.SubscribeAsync("own", MachineChangedOps.Subscribe(alices.Id));
            (await own.NextChangeAsync("own")).State.Should().NotBeNull("the owner may watch");
        }
        var system = await InsertSystemInstanceAsync();
        await using var ws = await GraphQLWs.ConnectAsync(A, E2EHost.BobKey);

        await ws.SubscribeAsync("1", MachineChangedOps.Subscribe(Guid.NewGuid()));
        await ws.SubscribeAsync("2", MachineChangedOps.Subscribe(alices.Id));
        await ws.SubscribeAsync("3", MachineChangedOps.Subscribe(system));
        var answers = new[]
        {
            await ws.FirstAnswerAsync("1"),
            await ws.FirstAnswerAsync("2"),
            await ws.FirstAnswerAsync("3"),
        };

        answers
            .Distinct()
            .Should()
            .ContainSingle(
                "a caller learns nothing about which ids exist, whose they are or that a system instance has one, "
                    + "per "
                    + Adr
            );
        answers[0].Should().NotContain("Naming", "no snapshot is sent");
    }

    [Test]
    public async Task The_read_rule_is_checked_once_at_subscribe_and_routing_follows_row_id()
    {
        var started = await A.StartWizardAsync(E2EHost.AliceKey);
        var principalReads = A.Services.GetRequiredService<PrincipalReads>();
        await using var ws = await GraphQLWs.ConnectAsync(A, E2EHost.AliceKey);
        await ws.SubscribeAsync("1", MachineChangedOps.Subscribe(started.Id));
        await ws.NextChangeAsync("1");
        var before = principalReads.Count;

        var final = await WriteManyAsync(B, started.Id, 5);
        var events = await ws.ReadUntilRevisionAsync("1", final);

        events[^1].Revision.Should().Be(final);
        (principalReads.Count - before)
            .Should()
            .Be(
                0,
                "the read rule is checked once, at subscribe; each event is routed by the row it watches, per "
                    + Adr
            );
    }

    [Test]
    public async Task A_principal_whose_read_rule_fails_is_refused_at_subscribe()
    {
        var alices = await A.StartWizardAsync(E2EHost.AliceKey);
        await using (var own = await GraphQLWs.ConnectAsync(A, E2EHost.AliceKey))
        {
            await own.SubscribeAsync("own", MachineChangedOps.Subscribe(alices.Id));
            (await own.NextChangeAsync("own")).State.Should().NotBeNull("the owner may watch");
        }
        await using var ws = await GraphQLWs.ConnectAsync(A, E2EHost.BobKey);

        await ws.SubscribeAsync("1", MachineChangedOps.Subscribe(alices.Id));
        var (refused, _, raw) = await ws.RefusalAsync("1");

        refused
            .Should()
            .BeTrue(
                "the wizard's watch rule is owner-only, so Bob is refused at subscribe, per " + Adr
            );
        raw.Should().NotContain("Naming", "and sees nothing of the instance");
    }

    #endregion

    #region Deletion and incarnations

    [Test]
    public async Task Delete_yields_gone_without_a_re_read_and_completes()
    {
        var started = await A.StartWizardAsync(E2EHost.AliceKey);
        await using var ws = await GraphQLWs.ConnectAsync(A, E2EHost.AliceKey);
        await ws.SubscribeAsync("1", MachineChangedOps.Subscribe(started.Id));
        await ws.NextChangeAsync("1");

        using (
            await B.PostAsync(
                WizardOps.Discard(started.Id, WizardOps.NewRequestId()),
                apiKey: E2EHost.AliceKey
            )
        ) { }
        var events = await ws.ReadUntilEndAsync("1");

        events[^1].Gone.Should().BeTrue("a deleted instance ends its stream with gone, per " + Adr);
        (await ws.NextAsync("1")).GetProperty("type").GetString().Should().Be("complete");
    }

    [Test]
    public async Task Expiry_by_the_sweeper_yields_gone()
    {
        var started = await A.StartWizardAsync(E2EHost.AliceKey);
        await using var ws = await GraphQLWs.ConnectAsync(A, E2EHost.AliceKey);
        await ws.SubscribeAsync("1", MachineChangedOps.Subscribe(started.Id));
        await ws.NextChangeAsync("1");

        // Held past the host's explicit draft lifetime.
        await E2ESql.ExecuteAsync(
            Database,
            "UPDATE trax.snapshot_draft SET updated_at = now() - interval '2 hours', "
                + "created_at = now() - interval '2 hours' WHERE machine = @machine AND id = @id",
            ("machine", WizardMachine.MachineId),
            ("id", started.Id)
        );
        var events = await ws.ReadUntilEndAsync("1", TimeSpan.FromMinutes(1));

        events[^1].Gone.Should().BeTrue("an expired draft ends its stream with gone, per " + Adr);
    }

    [Test]
    public async Task A_recreate_under_the_same_id_does_not_reach_a_stream_on_the_old_incarnation()
    {
        var started = await A.StartWizardAsync(E2EHost.AliceKey);
        var userKey = (
            await E2ESql.DraftAsync(Database, WizardMachine.MachineId, started.Id)
        )!.UserKey;
        await using var ws = await GraphQLWs.ConnectAsync(A, E2EHost.AliceKey);
        await ws.SubscribeAsync("old", MachineChangedOps.Subscribe(started.Id));
        await ws.NextChangeAsync("old");

        using (
            await A.PostAsync(
                WizardOps.Discard(started.Id, WizardOps.NewRequestId()),
                apiKey: E2EHost.AliceKey
            )
        ) { }
        (await ws.ReadUntilEndAsync("old"))[^1].Gone.Should().BeTrue();
        await E2ESql.ExecuteAsync(
            Database,
            "INSERT INTO trax.snapshot_draft (id, user_key, machine, version, state, context, "
                + "concurrency_token, updated_at, created_at, owner_kind) "
                + "VALUES (@id, @user, @machine, 1, 'Naming', '{\"title\":\"Again\"}'::jsonb, "
                + "gen_random_uuid(), now(), now(), 'user')",
            ("id", started.Id),
            ("user", userKey),
            ("machine", WizardMachine.MachineId)
        );

        await ws.SubscribeAsync("new", MachineChangedOps.Subscribe(started.Id));
        var reborn = await ws.NextChangeAsync("new");

        reborn
            .Revision.Should()
            .Be(
                1,
                "a re-created row is a new incarnation, whose revisions start again, per " + Adr
            );
        var old = await ws.TryNextAsync("old", TimeSpan.FromMilliseconds(200));
        (old is null || old.Value.GetProperty("type").GetString() == "complete")
            .Should()
            .BeTrue("the old incarnation's stream ended at gone and hears nothing of the new row");
    }

    #endregion

    #region Principals and versions

    [Test]
    public async Task Token_expiry_ends_the_stream()
    {
        var subject = $"expiring-{Guid.NewGuid():N}";
        var started = await StartAsJwtAsync(subject);
        var shortLived = Tokens.Mint(b =>
            b.WithSubject(subject).WithExpires(DateTime.UtcNow.AddSeconds(5))
        );
        await using var ws = await GraphQLWs.ConnectWithPayloadAsync(
            A,
            new { authToken = shortLived }
        );
        await ws.SubscribeAsync("1", MachineChangedOps.Subscribe(started));
        await ws.NextChangeAsync("1");

        var ended = false;
        var clock = Stopwatch.StartNew();
        try
        {
            while (!ended)
            {
                var message = await ws.NextAsync("1", TimeSpan.FromSeconds(30));
                ended = message.GetProperty("type").GetString() is "complete" or "error";
            }
        }
        catch (WebSocketException)
        {
            ended = true;
        }

        ended
            .Should()
            .BeTrue(
                "a stream lives no longer than the token that opened it, which bounds a role revoked "
                    + "mid-stream, per "
                    + Adr
            );
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task The_user_key_comes_from_the_socket_principal_under_jwt_and_api_key()
    {
        var executor = await A.ExecutorAsync();
        var subscriptions = executor.Schema.SubscriptionType;
        subscriptions.Should().NotBeNull("the host serves subscriptions");
        var field = subscriptions!.Fields.FirstOrDefault(f => f.Name == "machineChanged");
        field.Should().NotBeNull("machineChanged is a subscription, per " + Adr);
        field!
            .Arguments.Select(a => a.Name)
            .Should()
            .NotContain(
                name => name.Contains("user", StringComparison.OrdinalIgnoreCase),
                "the user key is never an argument"
            );

        var subject = $"jwt-user-{Guid.NewGuid():N}";
        var jwtOwned = await StartAsJwtAsync(subject);
        var keyOwned = await A.StartWizardAsync(E2EHost.AliceKey);
        await using var jwt = await GraphQLWs.ConnectWithPayloadAsync(
            A,
            new { authToken = Tokens.Mint(b => b.WithSubject(subject)) }
        );
        await using var key = await GraphQLWs.ConnectAsync(A, E2EHost.AliceKey);

        await jwt.SubscribeAsync("jwt-own", MachineChangedOps.Subscribe(jwtOwned));
        await key.SubscribeAsync("key-own", MachineChangedOps.Subscribe(keyOwned.Id));
        await key.SubscribeAsync("key-other", MachineChangedOps.Subscribe(jwtOwned));

        (await jwt.NextChangeAsync("jwt-own"))
            .State.Should()
            .NotBeNull("a token's subject watches its own instance");
        (await key.NextChangeAsync("key-own"))
            .State.Should()
            .NotBeNull("an API key's principal watches its own instance");
        (await key.RefusalAsync("key-other"))
            .Refused.Should()
            .BeTrue("the API key's principal is not the token's subject, whatever id it names");
    }

    [Test]
    public async Task An_older_hash_subscription_id_is_refused_client_outdated()
    {
        await E2EHost.RecreateDatabaseAsync(VersionsDatabase);
        IHost? a = null,
            b = null;
        try
        {
            a = await ShelfHosts.StartAsync(VersionsDatabase, ShelfShape.V1, "versions_a");
            var v1 = await MachineDocumentOps.IdOfAsync(
                VersionsDatabase,
                ShelfMachine.MachineId,
                MachineDocumentOps.MachineChanged
            );
            b = await ShelfHosts.StartAsync(VersionsDatabase, ShelfShape.V2, "versions_b");
            var id = await b.StartShelfAsync(E2EHost.AliceKey);
            await using var ws = await GraphQLWs.ConnectAsync(b, E2EHost.AliceKey);

            await ws.SubscribePersistedAsync("1", v1, new { id });
            var (refused, codes, raw) = await ws.RefusalAsync("1");

            refused.Should().BeTrue(raw);
            codes
                .Should()
                .Contain(
                    DraftRefusalCodes.ClientOutdated,
                    "an older version's subscription tells the client to reload, per " + Adr
                );
        }
        finally
        {
            await ShelfHosts.StopAsync(a);
            await ShelfHosts.StopAsync(b);
            await E2EHost.DropDatabaseAsync(VersionsDatabase);
        }
    }

    [Test]
    public async Task A_row_written_by_a_newer_ir_ends_an_older_nodes_stream_client_outdated()
    {
        await E2EHost.RecreateDatabaseAsync(VersionsDatabase);
        IHost? a = null,
            b = null;
        try
        {
            a = await ShelfHosts.StartAsync(VersionsDatabase, ShelfShape.V1, "versions_a");
            var id = await a.StartShelfAsync(E2EHost.AliceKey);
            await using var ws = await GraphQLWs.ConnectAsync(a, E2EHost.AliceKey);
            await ws.SubscribeAsync("1", MachineChangedOps.Subscribe(id, ShelfMachine.MachineId));
            await ws.NextChangeAsync("1");
            b = await ShelfHosts.StartAsync(VersionsDatabase, ShelfShape.V2, "versions_b");

            using (await b.SendShelfEventAsync(E2EHost.AliceKey, "file", id)) { }
            var events = await ws.ReadUntilEndAsync("1");

            events[^1]
                .ProblemCode.Should()
                .Be(
                    DraftRefusalCodes.ClientOutdated,
                    "the row now holds a version this node cannot serve, per " + Adr
                );
        }
        finally
        {
            await ShelfHosts.StopAsync(a);
            await ShelfHosts.StopAsync(b);
            await E2EHost.DropDatabaseAsync(VersionsDatabase);
        }
    }

    #endregion

    #region Socket limits

    [Test]
    public async Task Subscriptions_are_capped_per_principal_and_instance()
    {
        var started = await A.StartWizardAsync(E2EHost.AliceKey);
        var sockets = new List<GraphQLWs>();
        var accepted = new List<(GraphQLWs Socket, string Operation)>();
        IReadOnlyList<string>? refusal = null;
        try
        {
            // Spread over sockets, so the socket's own operation limit is never what refuses.
            for (var attempt = 0; attempt < 200 && refusal is null; attempt++)
            {
                if (attempt % 50 == 0)
                    sockets.Add(await GraphQLWs.ConnectAsync(A, E2EHost.AliceKey));
                var socket = sockets[^1];
                var operation = $"cap-{attempt}";
                await socket.SubscribeAsync(operation, MachineChangedOps.Subscribe(started.Id));
                if (attempt == 0)
                {
                    await socket.NextChangeAsync(operation);
                    accepted.Add((socket, operation));
                    continue;
                }
                var (refused, codes, _) = await socket.RefusalAsync(operation);
                if (refused)
                    refusal = codes;
                else
                    accepted.Add((socket, operation));
            }

            refusal
                .Should()
                .NotBeNull("one principal's subscriptions to one instance are capped, per " + Adr);
            refusal!
                .Should()
                .NotContain(SocketLimitCode)
                .And.NotBeEmpty("the refusal carries its own code");

            var (freed, op) = accepted[0];
            await freed.CompleteAsync(op);
            // The server frees the place once it has handled the complete, after this returns, so a
            // subscription sent straight away can still be refused. A refused one takes no place, so
            // try again until one is accepted.
            var tries = 0;
            (
                await E2EWait.UntilAsync(async () =>
                {
                    var retry = $"after-free-{tries++}";
                    await freed.SubscribeAsync(retry, MachineChangedOps.Subscribe(started.Id));
                    var (refused, _, _) = await freed.RefusalAsync(retry);
                    return !refused;
                })
            ).Should().BeTrue("a completed stream frees its place");
        }
        finally
        {
            foreach (var socket in sockets)
                await socket.DisposeAsync();
        }
    }

    [Test]
    public async Task machineChanged_counts_toward_the_100_operation_socket_cap()
    {
        // One instance started through the API, and the rest written beside it for the same user: the test is about
        // the socket's operations, not about starting.
        var first = (await A.StartWizardAsync(E2EHost.AliceKey)).Id;
        var userKey = (await E2ESql.DraftAsync(Database, WizardMachine.MachineId, first))!.UserKey;
        var instances = new List<Guid> { first };
        for (var i = 0; i < 100; i++)
            instances.Add(await InsertUserInstanceAsync(userKey!));
        await using var ws = await GraphQLWs.ConnectAsync(A, E2EHost.AliceKey);

        for (var i = 0; i < 100; i++)
        {
            await ws.SubscribeAsync($"s{i}", MachineChangedOps.Subscribe(instances[i]));
            await ws.NextChangeAsync($"s{i}");
        }
        await ws.SubscribeAsync("s100", MachineChangedOps.Subscribe(instances[100]));
        var past = await ws.NextAsync("s100");

        past.GetRawText()
            .Should()
            .Contain(
                SocketLimitCode,
                "a machineChanged stream holds one of the socket's 100 places, per api/0015 and "
                    + Adr
            );
    }

    // Green today, and kept as the guard: the upgrade is refused before any operation, machineChanged included.
    [Test]
    public async Task An_unlisted_origin_cannot_upgrade_for_machineChanged()
    {
        var client = A.GetTestServer().CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        client.ConfigureRequest = request => request.Headers["Origin"] = "https://unlisted.example";

        var upgrade = async () =>
        {
            using var socket = await client.ConnectAsync(
                new Uri("ws://localhost/trax/graphql"),
                CancellationToken.None
            );
        };

        (await upgrade.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("403", "a page from an unlisted origin cannot open a socket, per api/0007");
    }

    [Test]
    public async Task An_aborted_socket_releases_its_instance_watch()
    {
        var watched = await A.StartWizardAsync(E2EHost.AliceKey);
        var sentinel = await A.StartWizardAsync(E2EHost.AliceKey);
        var reads = A.Services.GetRequiredService<CountingDataContexts>();
        var aborted = await GraphQLWs.ConnectAsync(A, E2EHost.AliceKey);
        await aborted.SubscribeAsync("1", MachineChangedOps.Subscribe(watched.Id));
        await aborted.NextChangeAsync("1");
        await using var ws = await GraphQLWs.ConnectAsync(A, E2EHost.AliceKey);
        await ws.SubscribeAsync("sentinel", MachineChangedOps.Subscribe(sentinel.Id));
        await ws.NextChangeAsync("sentinel");

        aborted.Abort();
        aborted.State.Should().Be(WebSocketState.Aborted);

        // Once the watch is released, a write to the instance costs this node no read: of a write to the watched
        // instance and then one to the sentinel, only the sentinel's is read.
        var released = await E2EWait.UntilAsync(async () =>
        {
            var before = reads.Count;
            await WriteManyAsync(B, watched.Id, 1);
            var final = await WriteManyAsync(B, sentinel.Id, 1);
            await ws.ReadUntilRevisionAsync("sentinel", final);
            return reads.Count - before == 1;
        });

        released
            .Should()
            .BeTrue(
                "an aborted socket's watch is released, and nobody else watches the row, per " + Adr
            );
        await aborted.DisposeAsync();
    }

    [Test]
    public async Task An_anonymous_socket_is_refused()
    {
        var started = await A.StartWizardAsync(E2EHost.AliceKey);
        GraphQLWs ws;
        try
        {
            ws = await GraphQLWs.ConnectAsync(A, null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or WebSocketException)
        {
            return; // refused at the handshake
        }

        await using (ws)
        {
            await ws.SubscribeAsync("1", MachineChangedOps.Subscribe(started.Id));
            var (refused, codes, raw) = await ws.RefusalAsync("1");

            refused.Should().BeTrue(raw);
            codes
                .Should()
                .IntersectWith(
                    [
                        "AUTH_NOT_AUTHENTICATED",
                        "AUTH_NOT_AUTHORIZED",
                        "TRAX_AUTHORIZATION",
                        DraftRefusalCodes.Forbidden,
                    ],
                    "an anonymous socket is refused for who it is, per " + Adr
                );
        }
    }

    #endregion

    #region Notices (the change feed, as a subscriber sees it)

    [Test]
    public async Task A_forged_notice_for_an_unchanged_row_sends_nothing()
    {
        var watched = await A.StartWizardAsync(E2EHost.AliceKey);
        var sentinel = await A.StartWizardAsync(E2EHost.AliceKey);
        await using var ws = await GraphQLWs.ConnectAsync(A, E2EHost.AliceKey);
        await ws.SubscribeAsync("1", MachineChangedOps.Subscribe(watched.Id));
        await ws.NextChangeAsync("1");
        await ws.SubscribeAsync("sentinel", MachineChangedOps.Subscribe(sentinel.Id));
        await ws.NextChangeAsync("sentinel");

        await NotifyAsync(await RowIdAsync(watched.Id), times: 1);
        var final = await WriteManyAsync(B, sentinel.Id, 1);
        await ws.ReadUntilRevisionAsync("sentinel", final);

        (await ws.TryNextAsync("1", TimeSpan.FromMilliseconds(200)))
            .Should()
            .BeNull(
                "a notice is only a hint: the row's revision did not move, so nothing is sent, per "
                    + Adr
            );
    }

    [Test]
    public async Task N_subscribers_and_M_notices_on_one_row_cost_one_coalesced_read()
    {
        var watched = await A.StartWizardAsync(E2EHost.AliceKey);
        var sentinel = await A.StartWizardAsync(E2EHost.AliceKey);
        var reads = A.Services.GetRequiredService<CountingDataContexts>();
        await using var ws = await GraphQLWs.ConnectAsync(A, E2EHost.AliceKey);
        for (var i = 0; i < 3; i++)
        {
            await ws.SubscribeAsync($"n{i}", MachineChangedOps.Subscribe(watched.Id));
            await ws.NextChangeAsync($"n{i}");
        }
        await ws.SubscribeAsync("sentinel", MachineChangedOps.Subscribe(sentinel.Id));
        await ws.NextChangeAsync("sentinel");
        var rowId = await RowIdAsync(watched.Id);
        var before = reads.Count;

        await NotifyAsync(rowId, times: 20);
        var final = await WriteManyAsync(B, sentinel.Id, 1);
        await ws.ReadUntilRevisionAsync("sentinel", final);

        (reads.Count - before)
            .Should()
            .BeLessThanOrEqualTo(
                3,
                "three subscribers and twenty notices on one row cost one read coalesced to the latest (and at most "
                    + "one more for a notice that lands during it), plus the sentinel's, per "
                    + Adr
            );
    }

    [Test]
    public async Task A_row_the_host_cannot_parse_ends_that_stream_only()
    {
        var broken = await A.StartWizardAsync(E2EHost.AliceKey);
        var healthy = await A.StartWizardAsync(E2EHost.AliceKey);
        await using var ws = await GraphQLWs.ConnectAsync(A, E2EHost.AliceKey);
        await ws.SubscribeAsync("broken", MachineChangedOps.Subscribe(broken.Id));
        await ws.NextChangeAsync("broken");
        await ws.SubscribeAsync("healthy", MachineChangedOps.Subscribe(healthy.Id));
        await ws.NextChangeAsync("healthy");

        // As a newer version would leave it: a state this node's machine does not have.
        await E2ESql.ExecuteAsync(
            Database,
            "UPDATE trax.snapshot_draft SET state = 'StateFromANewerVersion' "
                + "WHERE machine = @machine AND id = @id",
            ("machine", WizardMachine.MachineId),
            ("id", broken.Id)
        );
        var ended = false;
        while (!ended)
        {
            var message = await ws.NextAsync("broken");
            var type = message.GetProperty("type").GetString();
            ended =
                type is "complete" or "error"
                || message.GetRawText().Contains("\"problem\":{", StringComparison.Ordinal);
        }
        var final = await WriteManyAsync(B, healthy.Id, 1);
        var events = await ws.ReadUntilRevisionAsync("healthy", final);

        events[^1]
            .Revision.Should()
            .Be(final, "an unreadable row ends its own stream and no other, per " + Adr);
    }

    #endregion

    // Sends `count` events to a wizard in Naming or Paying, alternating describe and back, as Alice, and returns the
    // last revision.
    private static async Task<long> WriteManyAsync(IHost node, Guid id, int count)
    {
        long revision = 0;
        for (var i = 0; i < count; i++)
        {
            var row = await E2ESql.DraftAsync(Database, WizardMachine.MachineId, id);
            var result =
                row?.State == nameof(WizardState.Paying)
                    ? await node.AdvanceWizardAsync(E2EHost.AliceKey, "back", id)
                    : await node.AdvanceWizardAsync(
                        E2EHost.AliceKey,
                        "describe",
                        id,
                        WizardOps.DetailsInput()
                    );
            revision = result.Revision ?? 0;
        }
        return revision;
    }

    private static async Task<long> RowIdAsync(Guid id) =>
        await E2ESql.ScalarAsync<long>(
            Database,
            "SELECT row_id FROM trax.snapshot_draft WHERE machine = @machine AND id = @id",
            ("machine", WizardMachine.MachineId),
            ("id", id)
        );

    // Sends notices by hand, each in its own statement, as a forger with database access could.
    private static async Task NotifyAsync(long rowId, int times)
    {
        await using var connection = new NpgsqlConnection(E2EHost.ConnectionString(Database));
        await connection.OpenAsync();
        for (var i = 0; i < times; i++)
        {
            await using var notify = new NpgsqlCommand(
                "SELECT pg_notify(@channel, @payload)",
                connection
            );
            notify.Parameters.AddWithValue("channel", Channel);
            notify.Parameters.AddWithValue("payload", rowId.ToString());
            await notify.ExecuteNonQueryAsync();
        }
    }

    // A row of the wizard owned by the user `userKey`, written by hand.
    private static async Task<Guid> InsertUserInstanceAsync(string userKey)
    {
        var id = Guid.NewGuid();
        await E2ESql.ExecuteAsync(
            Database,
            "INSERT INTO trax.snapshot_draft (id, user_key, machine, version, state, context, "
                + "concurrency_token, updated_at, created_at, owner_kind) "
                + "VALUES (@id, @user, @machine, 1, 'Naming', '{\"title\":\"Atlas\"}'::jsonb, "
                + "gen_random_uuid(), now(), now(), 'user')",
            ("id", id),
            ("user", userKey),
            ("machine", WizardMachine.MachineId)
        );
        return id;
    }

    // A system-owned row of the wizard, which no client may watch.
    private static async Task<Guid> InsertSystemInstanceAsync()
    {
        var id = Guid.NewGuid();
        await E2ESql.ExecuteAsync(
            Database,
            "INSERT INTO trax.snapshot_draft (id, user_key, machine, version, state, context, "
                + "concurrency_token, updated_at, created_at, owner_kind) "
                + "VALUES (@id, NULL, @machine, 1, 'Naming', '{}'::jsonb, gen_random_uuid(), now(), now(), "
                + "'system')",
            ("id", id),
            ("machine", WizardMachine.MachineId)
        );
        return id;
    }

    // Starts a wizard over HTTP as the token's subject and returns its id.
    private async Task<Guid> StartAsJwtAsync(string subject)
    {
        var client = A.GetTestServer().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(
                new { query = WizardOps.Start("Atlas", WizardOps.NewRequestId()) }
            ),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            Tokens.Mint(b => b.WithSubject(subject))
        );
        var response = await client.SendAsync(request);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return WizardOps.Result(doc, "start").Id;
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

/// <summary>
/// Counts how often a node reads the caller's user key for the state machines, which every check of an owner-only
/// rule does.
/// </summary>
public sealed class PrincipalReads
{
    private int _count;

    /// <summary>Reads so far.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>
    /// Replaces the host's <see cref="ISnapshotPrincipal"/> with one that counts its reads, and registers the
    /// counter.
    /// </summary>
    public static void Install(IServiceCollection services)
    {
        var reads = new PrincipalReads();
        services.AddSingleton(reads);
        services.AddScoped<ISnapshotPrincipal>(sp => new Counting(
            new TraxCallerSnapshotPrincipal(sp.GetRequiredService<TraxCaller>()),
            reads
        ));
    }

    private sealed class Counting(ISnapshotPrincipal inner, PrincipalReads reads)
        : ISnapshotPrincipal
    {
        public string? CurrentUserKey
        {
            get
            {
                Interlocked.Increment(ref reads._count);
                return inner.CurrentUserKey;
            }
        }
    }
}
