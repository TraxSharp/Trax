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
        new TraxGraphQLBuilder(services).UsePersistedOperations(po =>
            po.SingleNode().ExposeOperationsNamespace(false)
        );
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

    #region Management queries

    [Test]
    public async Task List_FirstPage_WithinBudget()
    {
        await MeasureAsync(
            "operations.persistedOperations.persistedOperations (first page)",
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
            "operations.persistedOperations.persistedOperations (tenant, active)",
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
            "operations.persistedOperations.persistedOperations (id prefix)",
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
            "operations.persistedOperations.persistedOperations (last page)",
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
            "operations.persistedOperations.persistedOperation (by id)",
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
            "operations.persistedOperations.persistedOperationHistory",
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
            "operations.persistedOperations.uploadPersistedOperation",
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
            "operations.persistedOperations.deactivatePersistedOperation",
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
            "operations.persistedOperations.restorePersistedOperation",
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
