using HotChocolate.AspNetCore.Instrumentation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// Thrown when a WebSocket upgrade to the Trax schema comes from an origin that is not allowed.
/// HotChocolate reports it as a session error; the response is <c>403</c>.
/// </summary>
internal sealed class SocketUpgradeRefusedException()
    : InvalidOperationException("The WebSocket upgrade origin is not allowed for this endpoint.");
