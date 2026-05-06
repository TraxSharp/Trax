using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Junctions.BeginTransaction;
using Trax.Effect.Data.Junctions.CommitTransaction;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Tests.Data.Sqlite.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

[TestFixture]
public class TransactionJunctionTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services.BuildServiceProvider();

    [Test]
    public async Task BeginTransaction_OpensTransactionOnDataContext()
    {
        var context = Scope.ServiceProvider.GetRequiredService<IDataContext>();
        var junction = new BeginTransaction(context);

        var result = await junction.Run(Unit.Default);

        result.Should().Be(Unit.Default);
        ((DbContext)context).Database.CurrentTransaction.Should().NotBeNull();

        await context.CommitTransaction();
    }

    [Test]
    public async Task CommitTransaction_AfterBegin_ClosesTransaction()
    {
        var context = Scope.ServiceProvider.GetRequiredService<IDataContext>();
        await new BeginTransaction(context).Run(Unit.Default);
        ((DbContext)context).Database.CurrentTransaction.Should().NotBeNull();

        var commit = new CommitTransaction(context);
        var result = await commit.Run(Unit.Default);

        result.Should().Be(Unit.Default);
        ((DbContext)context).Database.CurrentTransaction.Should().BeNull();
    }

    [Test]
    public async Task BeginThenCommit_ChainedThroughJunctions_LeavesNoOpenTransaction()
    {
        var context = Scope.ServiceProvider.GetRequiredService<IDataContext>();

        await new BeginTransaction(context).Run(Unit.Default);
        await new CommitTransaction(context).Run(Unit.Default);

        ((DbContext)context).Database.CurrentTransaction.Should().BeNull();
    }
}
