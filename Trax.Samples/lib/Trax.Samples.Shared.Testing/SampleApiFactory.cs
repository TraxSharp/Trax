using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Trax.Samples.Shared.Testing;

/// <summary>
/// Base <see cref="WebApplicationFactory{TEntryPoint}"/> for sample E2E suites. Points the host at a
/// dedicated test database by overriding the <c>TraxDatabase</c> connection string, so an E2E run
/// never touches a developer's local data.
/// </summary>
/// <typeparam name="TProgram">The host's <c>Program</c> entry point.</typeparam>
public abstract class SampleApiFactory<TProgram> : WebApplicationFactory<TProgram>
    where TProgram : class
{
    /// <summary>The connection string the host should use for the test run.</summary>
    protected abstract string ConnectionString { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseSetting("ConnectionStrings:TraxDatabase", ConnectionString);
    }
}
