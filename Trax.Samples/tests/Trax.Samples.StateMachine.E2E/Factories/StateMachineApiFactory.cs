using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Trax.Samples.Shared.Testing;
using Trax.Samples.StateMachine.E2E.Fixtures;
using Trax.Samples.StateMachine.E2E.Utilities;

namespace Trax.Samples.StateMachine.E2E.Factories;

/// <summary>
/// Boots the real StateMachine sample host against the <c>statemachine_e2e_tests</c> database, in
/// Development unless a subclass says otherwise. Defaults to the CI Postgres service on port 5432;
/// <c>TRAX_TEST_PG_PORT</c> moves it.
///
/// <para>The one change to the host is its <see cref="ICharge"/>: the sample's
/// <see cref="LoggingCharge"/> only logs, so the factory binds a <see cref="RecordingCharge"/> in its
/// place, which a test reads to count charges and see their amounts. It is the binding the sample says
/// a real host swaps (a payment gateway), so everything else under test is the sample's own wiring.</para>
/// </summary>
public class StateMachineApiFactory : SampleApiFactory<Trax.Samples.StateMachine.Api.Program>
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=statemachine_e2e_tests;Username=trax;Password=trax123;"
        + "Maximum Pool Size=10;Minimum Pool Size=0";

    public RecordingCharge Charges { get; } = new();

    protected override string ConnectionString => TestPostgres.WithPort(DefaultConnectionString);

    protected virtual string Environment => "Development";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment(Environment);
        builder.ConfigureTestServices(services => services.AddSingleton<ICharge>(Charges));
    }
}
