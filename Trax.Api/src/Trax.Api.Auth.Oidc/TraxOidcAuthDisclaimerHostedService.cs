using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Trax.Api.Auth.Oidc;

/// <summary>
/// Emits a one-shot warning on startup whenever Trax OIDC auth is wired up.
/// The message is intentional: hosts that deploy this package are agreeing to
/// the NO-WARRANTY terms of <c>SECURITY-DISCLAIMER.md</c>.
/// </summary>
internal sealed class TraxOidcAuthDisclaimerHostedService(ILoggerFactory loggerFactory)
    : IHostedService
{
    private const string DisclaimerMessage =
        "Trax OIDC auth enabled. Trax provides NO WARRANTY for security breaches against systems using this package. You are solely responsible. See SECURITY-DISCLAIMER.md.";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Trax.Api.Auth.Oidc");
        logger.LogWarning(DisclaimerMessage);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
