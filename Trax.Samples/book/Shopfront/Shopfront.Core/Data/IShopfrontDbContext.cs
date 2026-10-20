using Microsoft.EntityFrameworkCore;
using Shopfront.Data.Models;
using Trax.Effect.Data.Services.DomainContext;

namespace Shopfront.Data;

public interface IShopfrontDbContext : IDomainDataContext
{
    DbSet<Product> Products { get; }
    DbSet<Order> Orders { get; }
}
