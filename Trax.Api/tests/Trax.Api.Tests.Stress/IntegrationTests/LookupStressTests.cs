using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Queries;
using Trax.Api.Tests.Stress.Fixtures;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests.Stress.IntegrationTests;

/// <summary>
/// <c>operations.lookup</c> at millions of rows: an id of each shape an operator pastes (a run's
/// external id, a number, a subject key, an instance's GUID, and text that names nothing) against
/// the same seed as <see cref="AdminEndpointStressTests"/>. Every read is an exact match on an
/// indexed column, so each lookup should cost a handful of index probes whatever the table sizes.
/// </summary>
[TestFixture]
[Category("Stress")]
[Explicit(
    "Stress suite: seeds millions of rows. Run with dotnet test --filter TestCategory=Stress"
)]
public class LookupStressTests : StressTestSetup
{
    private static IOperationsService Operations(IServiceProvider sp) =>
        sp.GetRequiredService<IOperationsService>();

    private static IDataContextProviderFactory Factory(IServiceProvider sp) =>
        sp.GetRequiredService<IDataContextProviderFactory>();

    private Task MeasureLookup(
        string label,
        Func<IServiceProvider, CancellationToken, Task<string>> text,
        Action<IReadOnlyList<LookupMatch>> check
    )
    {
        string? term = null;
        return MeasureAsync(
            label,
            ListBudget,
            async (sp, ct) =>
            {
                term ??= await text(sp, ct);
                var matches = await new OperationsQueries().GetLookup(term, Operations(sp), ct);
                check(matches);
            }
        );
    }

    [Test]
    public Task Lookup_ARunsExternalId_WithinBudget() =>
        MeasureLookup(
            "operations.lookup (run external id)",
            async (sp, ct) =>
            {
                using var db = await Factory(sp).CreateDbContextAsync(ct);
                var id = Profile.Metadata / 2;
                return (
                    await db
                        .Metadatas.Where(m => m.Id == id)
                        .Select(m => m.ExternalId)
                        .FirstAsync(ct)
                ).Trim();
            },
            matches => matches.Should().Contain(m => m.Kind == LookupKind.Run)
        );

    [Test]
    public Task Lookup_ANumber_WithinBudget() =>
        MeasureLookup(
            "operations.lookup (number: run, manifest, entry, dead letter ids)",
            (_, _) => Task.FromResult((Profile.Manifests / 2).ToString()),
            matches =>
                matches.Select(m => m.Kind).Should().Contain([LookupKind.Run, LookupKind.Manifest])
        );

    [Test]
    public Task Lookup_ASubjectKey_WithinBudget() =>
        MeasureLookup(
            "operations.lookup (subject key)",
            async (sp, ct) =>
            {
                await ReadFieldStressTests.EnsureSubjectRowsAsync(sp, ct);
                return "subject-7";
            },
            matches =>
                matches
                    .Should()
                    .HaveCount(OperationsService.LookupMatchCap)
                    .And.OnlyContain(m => m.MatchedOn == "subjectKey")
        );

    [Test]
    public Task Lookup_AnInstanceGuid_WithinBudget() =>
        MeasureLookup(
            "operations.lookup (instance GUID)",
            async (sp, ct) =>
            {
                using var db = await Factory(sp).CreateDbContextAsync(ct);
                var count = await db.SnapshotDrafts.CountAsync(ct);
                count.Should().BePositive("the seed holds state-machine instances");
                return (
                    await db
                        .SnapshotDrafts.OrderBy(x => x.RowId)
                        .Skip(count / 2)
                        .Select(x => x.Id)
                        .FirstAsync(ct)
                ).ToString();
            },
            matches => matches.Should().Contain(m => m.Kind == LookupKind.MachineInstance)
        );

    [Test]
    public Task Lookup_TextThatNamesNothing_WithinBudget() =>
        MeasureLookup(
            "operations.lookup (no match)",
            (_, _) => Task.FromResult("nothing-" + Guid.NewGuid().ToString("N")),
            matches => matches.Should().BeEmpty()
        );
}
