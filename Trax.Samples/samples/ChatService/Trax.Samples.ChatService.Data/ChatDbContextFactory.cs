using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Trax.Samples.ChatService.Data;

public class ChatDbContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
    public ChatDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ChatDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=trax;Username=trax;Password=trax123"
        );
        return new ChatDbContext(optionsBuilder.Options);
    }
}
