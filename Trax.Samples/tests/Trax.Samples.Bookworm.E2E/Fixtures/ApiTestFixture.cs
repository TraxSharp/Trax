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
                "Bookworm test database not reachable. Start it with: docker compose up -d "
                    + "from the repository root, which provisions bookworm_e2e_tests on port "
                    + "5432, the same database and port CI provisions."
            );

        GraphQL = new GraphQLClient(SharedBookwormSetup.Factory.CreateClient());
    }
}
