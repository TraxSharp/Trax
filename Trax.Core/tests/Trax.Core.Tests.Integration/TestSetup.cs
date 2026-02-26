using Microsoft.Extensions.DependencyInjection;

namespace Trax.Core.Tests.Integration;

public abstract class TestSetup
{
    public IServiceProvider ServiceProvider { get; set; }

    public abstract IServiceProvider ConfigureServices(IServiceCollection services);

    [SetUp]
    public virtual async Task TestSetUp()
    {
        ServiceProvider = ConfigureServices(new ServiceCollection());
    }

    [TearDown]
    public async Task TestTearDown() { }
}
