using System.Reflection;
using System.Reflection.Emit;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>The base each generated probe request derives from.</summary>
public class PingProbeRequest : IGraphQLClientRequest<object>
{
    public string Query => "query Ping { ping }";
}
