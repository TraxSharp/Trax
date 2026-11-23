using HotChocolate.Execution.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.Services.BulkOperations;

namespace Trax.Api.GraphQL.Audit;

internal sealed class TraxAuditDisclaimerHostedService(ILoggerFactory loggerFactory)
    : IHostedService
{
    private const string Message =
        "Trax auth enabled. Trax provides NO WARRANTY for security breaches against systems using this package. You are solely responsible. See SECURITY-DISCLAIMER.md.";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        loggerFactory.CreateLogger("Trax.Api.GraphQL.Audit").LogWarning(Message);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
