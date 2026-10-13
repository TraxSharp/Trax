// What any snippet on any page may take as given: the host a reader's Program.cs already has.
// Joins every compilation except on a page whose front matter says `snippet_context: none`.
// A snippet that declares its own `builder` or `services` hides these, as a local hides a member.
//
// Nothing from Trax belongs here. A page shows its own Trax usings, or names them in its own
// context file, so a reader copying the page sees what to write (CompiledSnippetsTests).
global using static DocsHost;
using System.Reflection;

public static class DocsHost
{
    public static WebApplicationBuilder builder { get; } = WebApplication.CreateBuilder();

    public static IServiceCollection services => builder.Services;

    public static IConfiguration configuration => builder.Configuration;

    public static WebApplication app => builder.Build();

    public static IServiceProvider serviceProvider => app.Services;

    public static string connectionString => "Host=localhost;Database=trax";

    public static string rabbitMqConnectionString => "amqp://guest:guest@localhost:5672";

    public static CancellationToken ct => CancellationToken.None;

    public static CancellationToken cancellationToken => CancellationToken.None;

    public static Assembly assembly => typeof(DocsHost).Assembly;

    public static Assembly[] assemblies => [assembly];
}
