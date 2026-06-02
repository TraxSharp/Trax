using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Samples.Bookworm.Lending.Context;

namespace Trax.Samples.Bookworm.Lending.Extensions;

/// <summary>Registration for the lending data context.</summary>
public static class LendingServiceCollectionExtensions
{
    /// <summary>Registers <see cref="LendingDbContext"/> behind <see cref="ILendingDbContext"/> against PostgreSQL.</summary>
    public static IServiceCollection AddLendingDataContext(
        this IServiceCollection services,
        string connectionString
    ) =>
        services.AddDomainDataContext<ILendingDbContext, LendingDbContext>(options =>
            options.UseNpgsql(connectionString)
        );
}
