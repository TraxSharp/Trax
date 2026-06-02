using Trax.Samples.Bookworm.E2E.Utilities;

namespace Trax.Samples.Bookworm.E2E.Fixtures;

/// <summary>
/// Base for HTTP-level Bookworm tests. Skips at runtime (not fails) when the test database is
/// unavailable, and hands each test a fresh GraphQL client over the shared host.
/// </summary>
[TestFixture]
public abstract class ApiTestFixture
{
    protected GraphQLClient GraphQL { get; private set; } = null!;

    [SetUp]
    public void SetUp()
    {
        if (!SharedBookwormSetup.DatabaseAvailable)
            Assert.Ignore(
                "Bookworm test database not reachable. Start it with: docker run -d --name "
                    + "trax_bookworm_pg -e POSTGRES_USER=trax -e POSTGRES_PASSWORD=trax123 "
                    + "-e POSTGRES_DB=trax -p 5433:5432 postgres:16-alpine"
            );

        GraphQL = new GraphQLClient(SharedBookwormSetup.Factory.CreateClient());
    }
}
