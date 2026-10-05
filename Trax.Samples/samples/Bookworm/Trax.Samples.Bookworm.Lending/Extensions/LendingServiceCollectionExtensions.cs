using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Samples.Bookworm.Lending.Context;

namespace Trax.Samples.Bookworm.Lending.Extensions;

/// <summary>Registration for the lending data context.</summary>
public static class LendingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="LendingDbContext"/> behind <see cref="ILendingDbContext"/> against
    /// PostgreSQL, with <typeparamref name="TCaller"/> as the caller its owner-scope filters read.
    /// </summary>
    /// <remarks>
    /// Not <c>AddDomainDataContext</c>: that registers a pooled factory, and a pooled context can
    /// only be built from its options, so it could not receive the request's caller. This registers
    /// an unpooled, scoped factory instead, which builds each context with the scoped
    /// <see cref="ILendingCaller"/> of the request resolving it. The GraphQL query models resolve
    /// <see cref="LendingDbContext"/> from the request scope too, so they read through the same
    /// filters.
    /// </remarks>
    public static IServiceCollection AddLendingDataContext<TCaller>(
        this IServiceCollection services,
        string connectionString
    )
        where TCaller : class, ILendingCaller
    {
        services.AddScoped<ILendingCaller, TCaller>();
        services.AddDbContextFactory<LendingDbContext>(
            options => options.UseNpgsql(connectionString),
            ServiceLifetime.Scoped
        );
        services.AddScoped<ILendingDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<LendingDbContext>>().CreateDbContext()
        );
        return services;
    }
}
