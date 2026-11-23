using Microsoft.EntityFrameworkCore;

namespace Trax.Api.Tests.Paging;

public class PagingDbContext(DbContextOptions<PagingDbContext> options) : DbContext(options)
{
    public DbSet<PagedLedger> Ledgers => Set<PagedLedger>();
    public DbSet<PagedJournal> Journals => Set<PagedJournal>();
    public DbSet<PagedNotebook> Notebooks => Set<PagedNotebook>();
    public DbSet<PagedPamphlet> Pamphlets => Set<PagedPamphlet>();
}
