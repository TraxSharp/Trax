using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Trax.Samples.ChatService.E2E.Factories;

public class ChatServiceApiFactory : WebApplicationFactory<Trax.Samples.ChatService.Api.Program>
{
    public string TraxDbPath { get; } =
        Path.Combine(Path.GetTempPath(), $"trax_e2e_{Guid.NewGuid():N}.db");

    public string ChatDbPath { get; } =
        Path.Combine(Path.GetTempPath(), $"chat_e2e_{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:TraxDatabase", $"Data Source={TraxDbPath}");
        builder.UseSetting("ConnectionStrings:ChatDatabase", $"Data Source={ChatDbPath}");
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        foreach (var path in new[] { TraxDbPath, ChatDbPath })
        {
            if (File.Exists(path))
                File.Delete(path);
            if (File.Exists(path + "-wal"))
                File.Delete(path + "-wal");
            if (File.Exists(path + "-shm"))
                File.Delete(path + "-shm");
        }
    }
}
