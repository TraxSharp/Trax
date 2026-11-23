using Microsoft.EntityFrameworkCore;
using Trax.Api.GraphQL.DataLoaders.CrossSchema;
using Trax.Core.Extensions;
using Trax.Core.Testing;

namespace Trax.Api.GraphQL.Testing.Tests;

/// <summary>
/// A consumer repo with no cross-schema edges: it opts out of both scans, and every inherited
/// guard passes on a source tree that holds no resolver at all.
/// </summary>
[TestFixture]
public sealed class CrossSchemaGuardFixtureOptOutSelfTest : CrossSchemaGuardFixture
{
    private TempRepo? _repo;

    protected override ArchitectureGuardOptions Options =>
        new() { RepoRootOverride = _repo.NotNull().Root, SourceScanRoots = ["src"] };

    protected override bool ExpectsCrossSchemaResolvers => false;

    protected override bool ExpectsParentResolvers => false;

    [OneTimeSetUp]
    public void CreateRepoWithoutResolvers() =>
        _repo = new TempRepo()
            .Write("src/App/App.csproj", "<Project />")
            .Write("src/App/Book.cs", "public sealed class Book { public int Id { get; set; } }");

    [OneTimeTearDown]
    public void Cleanup() => _repo?.Dispose();
}
