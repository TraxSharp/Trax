using Microsoft.EntityFrameworkCore;

namespace Trax.Api.Tests.Paging;

public class UndefinedPagingDbContext(DbContextOptions<UndefinedPagingDbContext> options)
    : DbContext(options)
{
    public DbSet<PagedScroll> Scrolls => Set<PagedScroll>();
}
