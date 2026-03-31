using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Trax.Samples.ChatService.E2E.Factories;

public class ChatServiceApiFactory : WebApplicationFactory<Trax.Samples.ChatService.Api.Program>
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=chat_e2e_tests;Username=trax;Password=trax123;Maximum Pool Size=10";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:TraxDatabase", ConnectionString);
        builder.UseSetting("ConnectionStrings:ChatDatabase", ConnectionString);
    }
}
