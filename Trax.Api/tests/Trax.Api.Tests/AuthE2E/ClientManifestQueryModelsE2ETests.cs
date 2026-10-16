using AwesomeAssertions;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Effect.StateMachine;
using static Trax.Api.Tests.AuthE2E.AuthE2EHost;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// The client manifest a host with query models exports lists each one with its arguments, its answer and what its
/// entity requires, the types its arguments and answer reach, and which of those are filter or sort inputs, so
/// <c>trax client</c> can check and type a document that reads it.
/// <para>Enforces <c>Trax.Docs/adr/0053-a-client-document-is-registered-by-the-host-at-boot.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0053-a-client-document-is-registered-by-the-host-at-boot.md")]
[TestFixture]
[NonParallelizable]
public class ClientManifestQueryModelsE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0053-a-client-document-is-registered-by-the-host-at-boot.md";

    private const string Database = "trax_api_client_manifest_models";

    private IHost _host = null!;
    private ClientManifest _manifest = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        TestDbContext.EnsureSeeded(ConnectionString(Database));
        AuthzTestDbContext.EnsureSeeded(ConnectionString(Database));
        _host = await StartAsync(Schemes.ApiKey, Database);
        var path = Path.Combine(Path.GetTempPath(), $"trax-manifest-{Guid.NewGuid():N}.json");
        try
        {
            await ClientManifestExport.WriteAsync(_host.Services, path, CancellationToken.None);
            _manifest = ClientManifest.Read(await File.ReadAllTextAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [OneTimeTearDown]
    public void Down() => _host.Dispose();

    [Test]
    public void The_manifest_lists_each_query_model_with_its_arguments_and_answer()
    {
        var books = _manifest.QueryModels.Single(q => q.Field == "bookRecords");

        books.Namespace.Should().Be("library");
        books.Answer.Should().EndWith("Connection", "the model is paged");
        books
            .Arguments.Select(a => a.Name)
            .Should()
            .Contain(["first", "after", "where", "order"], "per " + Adr);
        _manifest
            .QueryModels.Single(q => q.Field == "ownedBooks")
            .Requires!.RoleSets.Should()
            .ContainSingle()
            .Which.Should()
            .Equal("Admin");
    }

    [Test]
    public void The_manifest_lists_the_types_a_query_model_reaches_and_marks_its_filter_inputs()
    {
        var where = _manifest
            .QueryModels.Single(q => q.Field == "bookRecords")
            .Arguments.Single(a => a.Name == "where")
            .Type;

        _manifest.Types.Should().Contain($"input {where}").And.NotContain("type DiscoverQueries");
        _manifest
            .FilterInputs.Should()
            .Contain(where)
            .And.Contain(
                n => n.Contains("OperationFilterInput"),
                "the inputs a filter is built from"
            );
    }
}
