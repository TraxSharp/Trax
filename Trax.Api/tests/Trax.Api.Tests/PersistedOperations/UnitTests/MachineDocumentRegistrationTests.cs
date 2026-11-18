using AwesomeAssertions;
using Trax.Api.GraphQL.PersistedOperations.Startup;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

/// <summary>
/// A host stopped again after its services were disposed (a test host torn down twice) must not fail its teardown on
/// the registration's disposed stopping token.
/// </summary>
[TestFixture]
public class MachineDocumentRegistrationTests
{
    [Test]
    public async Task Stopping_after_dispose_does_nothing()
    {
        var registration = new MachineDocumentRegistration(
            Null.Of<IServiceProvider>(),
            Null.Of<Trax.Api.GraphQL.PersistedOperations.Services.MachineDocumentRegistry>(),
            Null.Of<Trax.Api.GraphQL.PersistedOperations.Storage.DbPersistedOperationStorage>(),
            Null.Of<Trax.Api.GraphQL.PersistedOperations.Configuration.PersistedOperationsOptions>()
        );
        registration.Dispose();

        var stop = async () =>
        {
            await registration.StoppingAsync(CancellationToken.None).ConfigureAwait(false);
            await registration.StopAsync(CancellationToken.None).ConfigureAwait(false);
        };

        await stop.Should().NotThrowAsync();
        registration.Invoking(r => r.Dispose()).Should().NotThrow("disposing twice is a no-op");
    }
}
