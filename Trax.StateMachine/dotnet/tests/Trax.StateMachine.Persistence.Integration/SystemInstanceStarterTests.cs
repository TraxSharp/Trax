using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// A system machine served from features is started at its key when the host starts, and only once the host's startup
/// checks have passed: a host they refuse writes nothing.
/// </summary>
public class SystemInstanceStarterTests
{
    private string _dir = null!;
    private string _file = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"trax-system-start-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(Path.GetTempPath(), $"sm_system_start_{Guid.NewGuid():N}.db");
        var (irs, host) = PolicyRunnerTests.Definitions();
        foreach (var (id, ir) in irs)
            File.WriteAllText(Path.Combine(_dir, id + IrDirectory.IrSuffix), ir.ToJsonString());
        File.WriteAllText(Path.Combine(_dir, IrDirectory.HostFileName), host.ToJsonString());
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
        try
        {
            File.Delete(_file);
        }
        catch (IOException)
        {
            // Best effort: a lingering handle can hold the temp file; the OS reaps it later.
        }
    }

    [Test]
    public async Task A_host_its_startup_checks_refuse_starts_no_system_instance()
    {
        await using var provider = Build(withClash: true);

        var refused = await Starting(provider);

        refused
            .Should()
            .BeOfType<InvalidOperationException>()
            .Which.Message.Should()
            .Contain("p-shop");
        (await SystemRows(provider)).Should().Be(0, "nothing is written before the checks pass");
    }

    [Test]
    public async Task A_host_that_starts_starts_its_system_instance()
    {
        await using var provider = Build(withClash: false);

        (await Starting(provider)).Should().BeNull();

        (await SystemRows(provider)).Should().Be(1);
    }

    private ServiceProvider Build(bool withClash)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(e => e.UseSqlite($"Data Source={_file}"))
                .AddStateMachines(o => o.ServeFeatures(_dir))
        );
        if (withClash)
            services.AddSingleton(UserOwnedShop());
        return services.BuildServiceProvider();
    }

    // The shop as a user's machine: another machine type under the shop's id, which the id check refuses.
    private static IMachine UserOwnedShop()
    {
        var (irs, host) = PolicyRunnerTests.Definitions();
        var any = new JsonObject { ["anyAuthenticated"] = true };
        var shop = host["machines"]!["p-shop"]!.AsObject();
        shop["owner"] = "user";
        shop["start"] = any.DeepClone();
        shop["read"] = any.DeepClone();
        host.Remove("policies");
        var file = HostFile.Read(host.ToJsonString());
        return IrMachines.Create(new IrMachines.Source(irs["p-shop"], file.Machines["p-shop"]), []);
    }

    // Every hosted service's StartingAsync, in the order the host runs them; the refusal, or null.
    private static async Task<Exception?> Starting(IServiceProvider provider)
    {
        try
        {
            foreach (
                var service in provider
                    .GetServices<IHostedService>()
                    .OfType<IHostedLifecycleService>()
            )
                await service.StartingAsync(CancellationToken.None);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static async Task<int> SystemRows(IServiceProvider provider)
    {
        var id = SystemInstanceStarter.IdOf("p-shop", "p-shop/main");
        await using var scope = provider.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotDrafts.AsNoTracking()
            .CountAsync(d => d.Id == id);
    }
}
