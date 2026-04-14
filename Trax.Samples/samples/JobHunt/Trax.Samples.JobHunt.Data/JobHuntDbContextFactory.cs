using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Trax.Samples.JobHunt.Data;

/// <summary>
/// Design-time factory used by `dotnet ef migrations add`. The connection string
/// here is only used at design time to determine the provider, never at runtime.
/// At runtime the Hub configures the context via DI in Program.cs.
/// </summary>
public class JobHuntDbContextFactory : IDesignTimeDbContextFactory<JobHuntDbContext>
{
    public JobHuntDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<JobHuntDbContext>();
        builder.UseNpgsql(
            "Host=localhost;Port=5432;Database=jobhunt;Username=trax;Password=trax123"
        );
        return new JobHuntDbContext(builder.Options);
    }
}
