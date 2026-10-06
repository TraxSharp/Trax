using Microsoft.AspNetCore.Hosting;
using Trax.Samples.Auth.E2E.Fixtures;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Auth.E2E.Factories;

/// <summary>
/// Boots the real Auth sample host against the <c>auth_e2e_tests</c> database, in Development
/// unless a subclass says otherwise. Defaults to the CI Postgres service on port 5432;
/// <c>TRAX_TEST_PG_PORT</c> moves it.
/// </summary>
public class AuthApiFactory : SampleApiFactory<Trax.Samples.Auth.Program>
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=auth_e2e_tests;Username=trax;Password=trax123;"
        + "Maximum Pool Size=10;Minimum Pool Size=0";

    protected override string ConnectionString => TestPostgres.WithPort(DefaultConnectionString);

    protected virtual string Environment => "Development";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment(Environment);
    }
}
