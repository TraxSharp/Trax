using AwesomeAssertions;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.PersistedOperations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.GraphQL.PersistedOperations.GraphQL;
using Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.GraphQL.PersistedOperations.Storage.Validation;
using Trax.Api.Tests.Stress.Fixtures;
using Trax.Effect.Data.Services.IDataContextFactory;

namespace Trax.Api.Tests.Stress.IntegrationTests;

/// <summary>
/// SLA tests for the persisted-operations surface over a large catalog: the management queries
/// and mutations the dashboard's Persisted Operations page uses, and the per-request document
/// lookup every persisted request makes.
/// </summary>
/// <remarks>
/// The store is registered the way a GraphQL host registers it, through
/// <c>UsePersistedOperations</c>, with no document cache, so every lookup reaches the database.
/// Upserts are validated by the no-op validator: validating a document against the schema costs
/// the same whatever the table holds, and the point here is the table. The seed holds
/// <c>TRAX_STRESS_PERSISTED_OPS</c> operations (100,000 by default) across eleven tenant keys, one
/// in four retired, with one history row each.
/// </remarks>
[TestFixture]
[Category("Stress")]
[Explicit(
    "Stress suite: seeds millions of rows. Run with dotnet test --filter TestCategory=Stress"
)]
public class PersistedOperationStressTests : StressTestSetup
{
    /// <summary>
    /// Budget for the lookup a persisted request makes before it executes, which every request
    /// pays when the document cache is off or cold.
    /// </summary>
    private static readonly TimeSpan HotPathBudget = TimeSpan.FromMilliseconds(50);

    /// <summary>A seeded operation with no tenant (the seed gives <c>g % 11 == 0</c> no tenant) that is active.</summary>
    private const string SeededId = "StressOp22_v1";

    private const string UploadedId = "StressUpload_v1";

    protected override void ConfigureServices(IServiceCollection services)
    {
        new TraxGraphQLBuilder(services).UsePersistedOperations(po => po.SingleNode());
        services.Replace(
            ServiceDescriptor.Singleton<IPersistedOperationValidator>(
                new NoOpPersistedOperationValidator()
            )
        );
    }

    private static IDataContextProviderFactory Factory(IServiceProvider sp) =>
        sp.GetRequiredService<IDataContextProviderFactory>();

    private static IPersistedOperationStore Store(IServiceProvider sp) =>
        sp.GetRequiredService<IPersistedOperationStore>();

    #region Per-request lookup

    [Test]
    public async Task DocumentLookup_Uncached_WithinBudget()
    {
        await MeasureAsync(
            "persisted operation document lookup (uncached)",
            HotPathBudget,
            async (sp, ct) =>
            {
                var storage = (IOperationDocumentStorage)Store(sp);
                var document = await storage.TryReadAsync(new OperationDocumentId(SeededId), ct);
                document.Should().NotBeNull();
            }
        );
    }

    [Test]
    public async Task DocumentLookup_UnknownId_WithinBudget()
    {
        // A miss is what a caller probing ids produces, and it must not cost more than a hit.
        await MeasureAsync(
            "persisted operation document lookup (unknown id)",
            HotPathBudget,
            async (sp, ct) =>
            {
                var storage = (IOperationDocumentStorage)Store(sp);
                var document = await storage.TryReadAsync(
                    new OperationDocumentId("NoSuchOperation_v1"),
                    ct
                );
                document.Should().BeNull();
            }
        );
    }

    #endregion

    #region Machine document ids

    /// <summary>
    /// What one request costs per id kind under the reserved machine-document prefix, against the same catalog: a
    /// node's own document (read from the store and verified against its id), an id of a machine this node does not
    /// serve, and an unseen id of one it does (both answered without waiting on the store), beside an ordinary id.
    /// Prints the mean, median and 99th percentile of each.
    /// </summary>
    [Test]
    public async Task MachineDocumentIds_PerIdCost_Measured()
    {
        const int Rounds = 2_000;
        var sp = ServiceProvider;
        var storage = sp.GetRequiredService<DbPersistedOperationStorage>();
        var registry = sp.GetRequiredService<MachineDocumentRegistry>();
        var irHash = new string('a', MachineDocuments.IrHashLength);
        const string Text = "query StressMachineGet { __typename }";
        var own = new MachineDocument(
            MachineDocuments.IdOf("stress-machine", "get", irHash, Text),
            "stress-machine",
            "get",
            Text,
            irHash
        );
        var (versions, stored) = await storage.RegisterMachineDocumentsAsync(
            [own],
            TimeSpan.FromDays(7),
            CancellationToken.None
        );
        registry.Publish([own], versions, stored);
        var hot = (IOperationDocumentStorage)storage;
        Func<CancellationToken, Task<IReadOnlyDictionary<string, StoredMachineDocument>>> read =
            storage.ReadMachineDocumentVersionsAsync;

        var kinds = new (string Label, Func<int, Task> Run)[]
        {
            (
                "ordinary id (stored)",
                async _ =>
                    (await hot.TryReadAsync(new OperationDocumentId(SeededId), default))
                        .Should()
                        .NotBeNull()
            ),
            (
                "own machine document (read and verified)",
                async _ =>
                {
                    (await registry.ClassifyAsync(own.Id, read, default))
                        .Should()
                        .Be(MachineDocumentVerdict.Own);
                    (await hot.TryReadAsync(new OperationDocumentId(own.Id), default))
                        .Should()
                        .NotBeNull();
                }
            ),
            (
                "machine this node does not serve",
                async i =>
                    (
                        await registry.ClassifyAsync(
                            MachineDocuments.IdOf("unserved", "get", irHash, i.ToString()),
                            read,
                            default
                        )
                    )
                        .Should()
                        .NotBe(MachineDocumentVerdict.Own)
            ),
            (
                "unseen id of a served machine",
                async i =>
                    await registry.ClassifyAsync(
                        MachineDocuments.IdOf(
                            "stress-machine",
                            "get",
                            $"{i:x8}".PadLeft(MachineDocuments.IrHashLength, 'b'),
                            Text
                        ),
                        read,
                        default
                    )
            ),
        };

        foreach (var (label, run) in kinds)
        {
            await run(-1);
            var samples = new double[Rounds];
            for (var i = 0; i < Rounds; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await run(i);
                samples[i] = sw.Elapsed.TotalMilliseconds;
            }
            Array.Sort(samples);
            TestContext.Out.WriteLine(
                $"{label}: mean {samples.Average():F3}ms, p50 {samples[Rounds / 2]:F3}ms, "
                    + $"p99 {samples[(int)(Rounds * 0.99)]:F3}ms over {Rounds:N0} requests "
                    + $"({Profile.PersistedOperations:N0} stored operations)"
            );
            samples[(int)(Rounds * 0.99)]
                .Should()
                .BeLessThan(
                    HotPathBudget.TotalMilliseconds,
                    $"{label} is on every persisted request"
                );
        }
    }

    #endregion

    #region Management queries

    [Test]
    public async Task List_FirstPage_WithinBudget()
    {
        await MeasureAsync(
            "persistedOperations.persistedOperations (first page)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new PersistedOperationQueries().PersistedOperations(
                    Factory(sp),
                    ct,
                    take: 50
                );
                page.Items.Should().HaveCount(50);
                page.TotalCount.Should().BeGreaterThanOrEqualTo(Profile.PersistedOperations);
            }
        );
    }

    [Test]
    public async Task List_FilterByTenantAndActive_WithinBudget()
    {
        await MeasureAsync(
            "persistedOperations.persistedOperations (tenant, active)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new PersistedOperationQueries().PersistedOperations(
                    Factory(sp),
                    ct,
                    new PersistedOperationFilter(IsActive: true, TenantKey: "tenant-3"),
                    take: 50
                );
                page.Items.Should().NotBeEmpty();
                page.Items.Should().OnlyContain(p => p.IsActive && p.TenantKey == "tenant-3");
            }
        );
    }

    [Test]
    public async Task List_FilterByIdPrefix_WithinBudget()
    {
        await MeasureAsync(
            "persistedOperations.persistedOperations (id prefix)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new PersistedOperationQueries().PersistedOperations(
                    Factory(sp),
                    ct,
                    new PersistedOperationFilter(IdStartsWith: "StressOp4242"),
                    take: 50
                );
                page.Items.Should().NotBeEmpty();
            }
        );
    }

    [Test]
    public async Task List_LastPage_WithinBudget()
    {
        // The list pages by offset, so its last page reads past every row before it.
        await MeasureAsync(
            "persistedOperations.persistedOperations (last page)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new PersistedOperationQueries().PersistedOperations(
                    Factory(sp),
                    ct,
                    skip: Profile.PersistedOperations - 50,
                    take: 50
                );
                page.Items.Should().NotBeEmpty();
            }
        );
    }

    [Test]
    public async Task PointRead_WithinBudget()
    {
        await MeasureAsync(
            "persistedOperations.persistedOperation (by id)",
            ListBudget,
            async (sp, ct) =>
            {
                var row = await new PersistedOperationQueries().PersistedOperation(
                    SeededId,
                    Factory(sp),
                    ct
                );
                row.Should().NotBeNull();
            }
        );
    }

    [Test]
    public async Task History_WithinBudget()
    {
        await MeasureAsync(
            "persistedOperations.persistedOperationHistory",
            ListBudget,
            async (sp, ct) =>
            {
                var history = await new PersistedOperationQueries().PersistedOperationHistory(
                    SeededId,
                    Factory(sp),
                    ct
                );
                history.Should().NotBeEmpty();
            }
        );
    }

    #endregion

    #region Management mutations

    [Test]
    public async Task Upload_NewOperation_WithinBudget()
    {
        await MeasureWriteAsync(
            "persistedOperations.uploadPersistedOperation",
            ListBudget,
            () =>
                ExecSqlAsync(
                    "DELETE FROM trax.persisted_operation_history "
                        + $"WHERE tenant_key = '' AND id = '{UploadedId}'; "
                        + "DELETE FROM trax.persisted_operation "
                        + $"WHERE tenant_key = '' AND id = '{UploadedId}'"
                ),
            async (sp, ct) =>
            {
                var payload = await new PersistedOperationMutations().UploadPersistedOperation(
                    new UploadPersistedOperationInput(
                        UploadedId,
                        "query StressUpload { operations { health { status } } }"
                    ),
                    Store(sp),
                    ct
                );
                payload.Errors.Should().BeEmpty();
            }
        );
    }

    [Test]
    public async Task Deactivate_WithinBudget()
    {
        await MeasureWriteAsync(
            "persistedOperations.deactivatePersistedOperation",
            ListBudget,
            ReactivateSeededOperation,
            async (sp, ct) =>
                (
                    await new PersistedOperationMutations().DeactivatePersistedOperation(
                        new DeactivatePersistedOperationInput(SeededId, "stress"),
                        Store(sp),
                        ct
                    )
                )
                    .Success.Should()
                    .BeTrue()
        );
    }

    [Test]
    public async Task Restore_WithinBudget()
    {
        await MeasureWriteAsync(
            "persistedOperations.restorePersistedOperation",
            ListBudget,
            () =>
                ExecSqlAsync(
                    "UPDATE trax.persisted_operation SET is_active = false, "
                        + $"deprecation_reason = 'stress' WHERE tenant_key = '' AND id = '{SeededId}'"
                ),
            async (sp, ct) =>
                (
                    await new PersistedOperationMutations().RestorePersistedOperation(
                        new RestorePersistedOperationInput(SeededId),
                        Store(sp),
                        Factory(sp),
                        ct
                    )
                )
                    .Success.Should()
                    .BeTrue(),
            restore: ReactivateSeededOperation
        );
    }

    /// <summary>Puts the seeded operation back as the seed left it: active, one history row.</summary>
    private static Task ReactivateSeededOperation() =>
        ExecSqlAsync(
            "DELETE FROM trax.persisted_operation_history WHERE tenant_key = '' "
                + $"AND id = '{SeededId}' AND change_type <> 'Upsert'; "
                + "UPDATE trax.persisted_operation SET is_active = true, deprecation_reason = NULL "
                + $"WHERE tenant_key = '' AND id = '{SeededId}'"
        );

    #endregion
}
