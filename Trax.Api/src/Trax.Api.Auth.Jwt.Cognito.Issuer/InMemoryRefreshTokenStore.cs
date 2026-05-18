using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Trax.Api.Auth.Jwt.Cognito.Issuer;

/// <summary>
/// In-memory <see cref="IRefreshTokenStore"/> for tests and local-development
/// auth servers. Tokens are 256-bit random byte strings, base64url-encoded;
/// state lives in process memory and is lost on restart.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// Not suitable for production. The implementation makes no attempt to
/// constant-time compare token values (tokens are dictionary keys), and a
/// process restart wipes every session.
/// </para>
/// </remarks>
public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private readonly ConcurrentDictionary<string, Entry> _tokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, Chain> _chains = new();
    private readonly TimeProvider _clock;

    /// <summary>Construct a store with the supplied clock; defaults to system time.</summary>
    public InMemoryRefreshTokenStore(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<RefreshTokenHandle> IssueAsync(
        Guid sub,
        string clientId,
        TimeSpan lifetime,
        CancellationToken ct
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        if (lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Lifetime must be positive.");

        var expiresAt = _clock.GetUtcNow() + lifetime;
        var chainId = Guid.NewGuid();
        _chains[chainId] = new Chain(sub, clientId, Revoked: false);

        var handle = Mint(chainId, sub, clientId, expiresAt);
        return Task.FromResult(handle);
    }

    /// <inheritdoc />
    public Task<RefreshTokenClaims?> ValidateAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token) || !_tokens.TryGetValue(token, out var entry))
            return Task.FromResult<RefreshTokenClaims?>(null);

        if (
            entry.Consumed
            || IsChainRevoked(entry.ChainId)
            || entry.ExpiresAt <= _clock.GetUtcNow()
        )
            return Task.FromResult<RefreshTokenClaims?>(null);

        return Task.FromResult<RefreshTokenClaims?>(
            new RefreshTokenClaims(entry.Sub, entry.ClientId, entry.ExpiresAt)
        );
    }

    /// <inheritdoc />
    public Task<RefreshTokenHandle?> RotateAsync(string oldToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(oldToken) || !_tokens.TryGetValue(oldToken, out var entry))
            return Task.FromResult<RefreshTokenHandle?>(null);

        if (
            entry.Consumed
            || IsChainRevoked(entry.ChainId)
            || entry.ExpiresAt <= _clock.GetUtcNow()
        )
            return Task.FromResult<RefreshTokenHandle?>(null);

        // Atomically transition the entry to consumed. If somebody else got
        // here first (double-rotation), reject this caller.
        var consumed = entry with
        {
            Consumed = true,
        };
        if (!_tokens.TryUpdate(oldToken, consumed, entry))
            return Task.FromResult<RefreshTokenHandle?>(null);

        var handle = Mint(entry.ChainId, entry.Sub, entry.ClientId, entry.ExpiresAt);
        return Task.FromResult<RefreshTokenHandle?>(handle);
    }

    /// <inheritdoc />
    public Task RevokeAsync(string token, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(token) && _tokens.TryGetValue(token, out var entry))
            RevokeChain(entry.ChainId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RevokeAllAsync(Guid sub, string clientId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        foreach (var (chainId, chain) in _chains)
        {
            if (
                chain.Sub == sub
                && string.Equals(chain.ClientId, clientId, StringComparison.Ordinal)
            )
                RevokeChain(chainId);
        }
        return Task.CompletedTask;
    }

    private RefreshTokenHandle Mint(
        Guid chainId,
        Guid sub,
        string clientId,
        DateTimeOffset expiresAt
    )
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        _tokens[token] = new Entry(chainId, sub, clientId, expiresAt, Consumed: false);
        return new RefreshTokenHandle(token, expiresAt);
    }

    private bool IsChainRevoked(Guid chainId) =>
        _chains.TryGetValue(chainId, out var chain) && chain.Revoked;

    private void RevokeChain(Guid chainId)
    {
        _chains.AddOrUpdate(
            chainId,
            _ => new Chain(default, string.Empty, Revoked: true),
            (_, existing) => existing with { Revoked = true }
        );
    }

    private sealed record Entry(
        Guid ChainId,
        Guid Sub,
        string ClientId,
        DateTimeOffset ExpiresAt,
        bool Consumed
    );

    private sealed record Chain(Guid Sub, string ClientId, bool Revoked);
}
