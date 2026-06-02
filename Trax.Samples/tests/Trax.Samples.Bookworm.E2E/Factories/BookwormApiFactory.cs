using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Bookworm.E2E.Factories;

/// <summary>
/// Boots the Bookworm API against a dedicated test PostgreSQL database. Defaults to the CI Postgres
/// service (port 5432, database <c>bookworm_e2e_tests</c>), matching the other sample E2E suites. Set
/// the <c>BOOKWORM_TEST_DB</c> environment variable to point at a throwaway local instance instead
/// (e.g. the port-5433 container in the test docs), which keeps E2E runs off any shared database.
/// </summary>
public sealed class BookwormApiFactory : SampleApiFactory<Api.Program>
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=bookworm_e2e_tests;Username=trax;Password=trax123;"
        + "Maximum Pool Size=8;Minimum Pool Size=0";

    protected override string ConnectionString =>
        Environment.GetEnvironmentVariable("BOOKWORM_TEST_DB") ?? DefaultConnectionString;
}
