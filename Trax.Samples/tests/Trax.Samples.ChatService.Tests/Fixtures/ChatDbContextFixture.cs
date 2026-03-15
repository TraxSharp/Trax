using Microsoft.EntityFrameworkCore;
using Trax.Samples.ChatService.Data;

namespace Trax.Samples.ChatService.Tests.Fixtures;

public static class ChatDbContextFixture
{
    public static ChatDbContext Create()
    {
        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ChatDbContext(options);
    }
}
