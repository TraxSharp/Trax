using System.Runtime.CompilerServices;
using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Subscriptions;
using HotChocolate.AspNetCore.Subscriptions.Protocols;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;
using Trax.Api.GraphQL.Authorization;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Extensions;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// The socket session interceptor <c>AddTraxGraphQL()</c> registers for every host. It
/// authenticates a subscription's <c>connection_init</c> with whichever token schemes the host
/// registered, API key, JWT, or both, by delegating each connection to
/// <see cref="TraxApiKeySocketInterceptor"/>, <see cref="TraxJwtDispatcherSocketInterceptor"/> or
/// <see cref="TraxJwtSocketInterceptor"/>.
/// </summary>
/// <remarks>
/// HotChocolate runs one <see cref="ISocketSessionInterceptor"/> per schema, so a host with both
/// schemes needs one interceptor that can serve either credential.
/// <para>
/// <b>Which schemes are active</b> is read from the completed application container on the first
/// connection, not from the <c>IServiceCollection</c> while <c>AddTraxGraphQL()</c> runs. A scheme
/// registered after <c>AddTraxGraphQL()</c> is therefore enforced like one registered before it.
/// </para>
/// <para>
/// <b>Routing.</b> With one scheme registered, every connection goes to it, exactly as the
/// scheme's own interceptor would handle it. With both, the payload decides: an <c>authToken</c>
/// that is a well-formed JWT goes to JWT validation and any other <c>authToken</c> to the
/// API-key resolver; with no <c>authToken</c>, an <c>apiKey</c> goes to the API-key resolver and
/// a <c>bearer</c> to JWT validation. A connection with none of the three is rejected. A
/// credential is validated by one strategy only; a JWT that fails validation is not retried as
/// an API key.
/// </para>
/// <para>
/// <b>No token scheme registered:</b> the connection is accepted, as HotChocolate's default
/// interceptor accepts it. A host with public subscriptions, or with cookie authentication
/// (which authenticates the WebSocket upgrade itself), is unaffected.
/// </para>
/// <para>
/// <b>Operations per connection.</b> A connection runs at most
/// <c>MaxOperationsPerConnection</c> operations at once (100 unless the GraphQL builder sets
/// another). An operation started past the limit is marked, and
/// <see cref="SocketOperationLimitRequestMiddleware"/> answers it with a
/// <c>TRAX_SOCKET_OPERATION_LIMIT</c> error; it takes no place. A place frees when an operation
/// completes. See <c>docs/adr/0015-a-socket-runs-a-bounded-number-of-operations.md</c>.
/// </para>
/// <para>
/// <b>Lifetime.</b> An accepted connection is closed with Going Away (1001) at its maximum
/// lifetime, one hour unless the GraphQL builder sets another; a JWT connection closes earlier at
/// its token's <c>exp</c>, and a cookie connection at its sign-in's expiry, both with
/// <c>PolicyViolation</c> (1008). An API-key connection's key is re-resolved every five minutes
/// (or the builder's interval) and the connection is closed with <c>PolicyViolation</c> once it
/// no longer resolves to the same principal. See
/// <c>docs/adr/0033-a-socket-connection-has-a-maximum-lifetime-and-re-checks-its-key.md</c>.
/// </para>
/// <para>
/// A host that supplies its own interceptor through
/// <c>ConfigureSchema(b =&gt; b.AddSocketSessionInterceptor&lt;T&gt;())</c> replaces this one.
/// </para>
/// </remarks>
public sealed class TraxCompositeSocketInterceptor : DefaultSocketSessionInterceptor
{
    /// <summary>The operations a connection runs at once when the builder sets no limit.</summary>
    internal const int DefaultMaxOperationsPerConnection = 100;

    /// <summary>How long a connection stays open when the builder sets no lifetime.</summary>
    internal static readonly TimeSpan DefaultMaxConnectionLifetime = TimeSpan.FromHours(1);

    /// <summary>How often an API-key connection's key is re-resolved when the builder sets nothing.</summary>
    internal static readonly TimeSpan DefaultCredentialRecheckInterval = TimeSpan.FromMinutes(5);

    /// <summary>The longest lifetime or re-check interval a host may set.</summary>
    internal static readonly TimeSpan LongestConnectionDuration = TimeSpan.FromDays(1);

    private readonly TraxApplicationServices _applicationServices;
    private readonly Lazy<Strategies> _strategies;
    private readonly int _maxOperationsPerConnection;
    private readonly TimeSpan _maxConnectionLifetime;
    private readonly TimeSpan _credentialRecheckInterval;

    // The ids of the operations each connection is running. Keyed weakly, so a closed
    // connection's entry goes with it.
    private readonly ConditionalWeakTable<ISocketSession, HashSet<string>> _running = new();

    /// <summary>
    /// Creates the interceptor over the application container, which it reads for the registered
    /// schemes on first use. A connection runs at most 100 operations at once.
    /// </summary>
    public TraxCompositeSocketInterceptor(TraxApplicationServices applicationServices)
        : this(applicationServices, DefaultMaxOperationsPerConnection) { }

    /// <summary>
    /// Creates the interceptor with the operations limit the GraphQL builder set.
    /// </summary>
    internal TraxCompositeSocketInterceptor(
        TraxApplicationServices applicationServices,
        int maxOperationsPerConnection
    )
        : this(
            applicationServices,
            maxOperationsPerConnection,
            DefaultMaxConnectionLifetime,
            DefaultCredentialRecheckInterval
        ) { }

    /// <summary>
    /// Creates the interceptor with the operations limit, connection lifetime and credential
    /// re-check interval the GraphQL builder set.
    /// </summary>
    internal TraxCompositeSocketInterceptor(
        TraxApplicationServices applicationServices,
        int maxOperationsPerConnection,
        TimeSpan maxConnectionLifetime,
        TimeSpan credentialRecheckInterval
    )
    {
        ArgumentNullException.ThrowIfNull(applicationServices);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxOperationsPerConnection);
        RequireConnectionDuration(maxConnectionLifetime, nameof(maxConnectionLifetime));
        RequireConnectionDuration(credentialRecheckInterval, nameof(credentialRecheckInterval));
        _applicationServices = applicationServices;
        _maxOperationsPerConnection = maxOperationsPerConnection;
        _maxConnectionLifetime = maxConnectionLifetime;
        _credentialRecheckInterval = credentialRecheckInterval;
        _strategies = new Lazy<Strategies>(
            DiscoverStrategies,
            LazyThreadSafetyMode.ExecutionAndPublication
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// After the credential is accepted, the endpoint policy set with
    /// <c>RequireAuthorization(...)</c> on the GraphQL builder, when there is one, is evaluated
    /// against the connection's principal. An anonymous connection does not satisfy it.
    /// </remarks>
    public override async ValueTask<ConnectionStatus> OnConnectAsync(
        ISocketSession session,
        IOperationMessagePayload connectionInitMessage,
        CancellationToken cancellationToken = default
    )
    {
        var status = await AuthenticateAsync(session, connectionInitMessage, cancellationToken)
            .ConfigureAwait(false);

        if (!status.Accepted)
            return status;

        if (!await SatisfiesEndpointPolicyAsync(session).ConfigureAwait(false))
            return ConnectionStatus.Reject("Not authorized.");

        BoundLifetime(session);
        return status;
    }

    /// <summary>
    /// Throws unless <paramref name="value"/> is positive and at most
    /// <see cref="LongestConnectionDuration"/>. Shared with the GraphQL builder.
    /// </summary>
    internal static void RequireConnectionDuration(TimeSpan value, string paramName)
    {
        if (value <= TimeSpan.Zero || value > LongestConnectionDuration)
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be positive and at most {LongestConnectionDuration.TotalHours:0} hours."
            );
    }

    /// <summary>
    /// Closes an accepted connection at its maximum lifetime, and, when the upgrade request's own
    /// authentication (a cookie) carries an expiry, when that expires. A JWT connection's
    /// strategy has already set its token's <c>exp</c>; the earliest deadline wins. See
    /// <c>docs/adr/0033-a-socket-connection-has-a-maximum-lifetime-and-re-checks-its-key.md</c>.
    /// </summary>
    private void BoundLifetime(ISocketSession session)
    {
        var services = _applicationServices.Services;
        var time = services.GetService<TimeProvider>() ?? TimeProvider.System;
        var lifetime = SocketConnectionLifetime.For(
            session.Connection,
            time,
            services.GetService<ILoggerFactory>()?.CreateLogger<TraxCompositeSocketInterceptor>()
                ?? NullLogger<TraxCompositeSocketInterceptor>.Instance
        );

        lifetime.CloseAt(
            SocketConnectionLifetime.Add(time.GetUtcNow(), _maxConnectionLifetime),
            ConnectionCloseReason.EndpointUnavailable,
            SocketConnectionLifetime.LifetimeMessage
        );

        // Only when no token scheme authenticated the connection is the upgrade request's own
        // sign-in the connection's credential, as SignalR's CloseOnAuthenticationExpiration reads it.
        var strategies = _strategies.Value;
        if (
            strategies.ApiKey is null
            && strategies.Jwt is null
            && session
                .Connection.HttpContext?.Features.Get<IAuthenticateResultFeature>()
                ?.AuthenticateResult
                is { Succeeded: true, Properties.ExpiresUtc: { } expires }
        )
            lifetime.CloseAt(expires, ConnectionCloseReason.PolicyViolation, SignInExpiredMessage);
    }

    /// <summary>The close message a connection receives when its sign-in expires.</summary>
    internal const string SignInExpiredMessage = "The sign-in has expired.";

    /// <inheritdoc />
    /// <remarks>
    /// Admits the operation when the connection runs fewer than the limit, and otherwise marks the
    /// request so the pipeline refuses it. HotChocolate masks an exception thrown here, so the
    /// refusal is raised in the pipeline, where it reaches the client as a coded error.
    /// </remarks>
    public override ValueTask OnRequestAsync(
        ISocketSession session,
        string operationSessionId,
        OperationRequestBuilder requestBuilder,
        CancellationToken cancellationToken = default
    )
    {
        if (!TryAdmit(session, operationSessionId))
            requestBuilder.SetGlobalState(SocketOperationLimitRequestMiddleware.ExceededKey, true);

        return base.OnRequestAsync(session, operationSessionId, requestBuilder, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>Frees the place the operation held.</remarks>
    public override ValueTask OnCompleteAsync(
        ISocketSession session,
        string operationSessionId,
        CancellationToken cancellationToken = default
    )
    {
        if (_running.TryGetValue(session, out var running))
            lock (running)
                running.Remove(operationSessionId);

        return base.OnCompleteAsync(session, operationSessionId, cancellationToken);
    }

    private bool TryAdmit(ISocketSession session, string operationSessionId)
    {
        var running = _running.GetValue(session, static _ => []);
        lock (running)
        {
            if (running.Count >= _maxOperationsPerConnection)
                return false;
            running.Add(operationSessionId);
            return true;
        }
    }

    private ValueTask<ConnectionStatus> AuthenticateAsync(
        ISocketSession session,
        IOperationMessagePayload connectionInitMessage,
        CancellationToken cancellationToken
    )
    {
        var strategies = _strategies.Value;

        if (strategies.ApiKey is null && strategies.Jwt is null)
            return base.OnConnectAsync(session, connectionInitMessage, cancellationToken);

        var target = SelectStrategy(strategies, connectionInitMessage);
        if (target is null)
            return new ValueTask<ConnectionStatus>(
                ConnectionStatus.Reject("Missing auth token in connection_init payload.")
            );

        return target.OnConnectAsync(session, connectionInitMessage, cancellationToken);
    }

    /// <summary>
    /// True when no endpoint policy is set, or the connection's principal satisfies it. Checking
    /// at the handshake refuses a connection that could run nothing; every operation it carries is
    /// checked again in the request pipeline.
    /// </summary>
    private async ValueTask<bool> SatisfiesEndpointPolicyAsync(ISocketSession session)
    {
        var services = _applicationServices.Services;
        var configuration = services.GetService<GraphQLConfiguration>();
        if (configuration?.AuthorizationRequired != true)
            return true;

        return await EndpointPolicy
            .IsSatisfiedAsync(
                services.GetRequiredService<IAuthorizationService>(),
                configuration,
                session.Connection.HttpContext?.User
            )
            .ConfigureAwait(false);
    }

    private static DefaultSocketSessionInterceptor? SelectStrategy(
        Strategies strategies,
        IOperationMessagePayload connectionInitMessage
    )
    {
        // One scheme: it reads the payload itself, and rejects a missing credential in its own
        // words, so the composite changes nothing for a single-scheme host.
        if (strategies.Jwt is null)
            return strategies.ApiKey;
        if (strategies.ApiKey is null)
            return strategies.Jwt;

        var payload = ConnectionInitPayloadReader.TryRead<ConnectionInitPayload>(
            connectionInitMessage
        );

        // The precedence matches each strategy's own read (authToken first), so the credential
        // routed on is the credential the strategy then validates.
        if (!string.IsNullOrWhiteSpace(payload?.AuthToken))
            return LooksLikeJwt(payload.AuthToken) ? strategies.Jwt : strategies.ApiKey;
        if (!string.IsNullOrWhiteSpace(payload?.ApiKey))
            return strategies.ApiKey;
        if (!string.IsNullOrWhiteSpace(payload?.Bearer))
            return strategies.Jwt;

        return null;
    }

    /// <summary>
    /// True when <paramref name="token"/> parses as a compact JWT, header included. Says nothing
    /// about validity: the JWT strategy validates it in full. Parsing the header, rather than
    /// matching the three-segment shape, keeps an API key that happens to contain two dots on
    /// the API-key path.
    /// </summary>
    internal static bool LooksLikeJwt(string token)
    {
        try
        {
            _ = new JsonWebToken(token);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenMalformedException)
        {
            return false;
        }
    }

    private Strategies DiscoverStrategies()
    {
        var services = _applicationServices.Services;
        var isService = services.GetRequiredService<IServiceProviderIsService>();
        var loggerFactory = services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;

        DefaultSocketSessionInterceptor? apiKey = null;
        if (isService.IsService(typeof(ITraxPrincipalResolver<string>)))
            apiKey = new TraxApiKeySocketInterceptor(
                _applicationServices,
                loggerFactory.CreateLogger<TraxApiKeySocketInterceptor>(),
                _credentialRecheckInterval
            );

        // A dispatcher routes every mapped JWT scheme by issuer, so it supersedes the
        // single-scheme interceptor when both are registered.
        DefaultSocketSessionInterceptor? jwt = null;
        if (isService.IsService(typeof(JwtDispatcherRuntime)))
            jwt = new TraxJwtDispatcherSocketInterceptor(
                services.GetRequiredService<JwtDispatcherRuntime>(),
                services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>(),
                _applicationServices,
                loggerFactory.CreateLogger<TraxJwtDispatcherSocketInterceptor>()
            );
        else if (services.GetService<JwtResolverRegistry>() is { SchemeNames.Count: > 0 } registry)
            // Every scheme AddTraxJwtAuth registered, named or default, authenticates the socket.
            jwt = new TraxJwtSchemesSocketInterceptor(
                registry,
                _applicationServices,
                loggerFactory.CreateLogger<TraxJwtSchemesSocketInterceptor>()
            );
        else if (isService.IsService(typeof(ITraxPrincipalResolver<JwtTokenInput>)))
            // A host that registered its own JWT resolver without AddTraxJwtAuth.
            jwt = new TraxJwtSocketInterceptor(
                services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>(),
                _applicationServices,
                loggerFactory.CreateLogger<TraxJwtSocketInterceptor>()
            );

        return new Strategies(apiKey, jwt);
    }

    private sealed record Strategies(
        DefaultSocketSessionInterceptor? ApiKey,
        DefaultSocketSessionInterceptor? Jwt
    );

    internal sealed record ConnectionInitPayload(string? AuthToken, string? ApiKey, string? Bearer);
}
