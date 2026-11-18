using System.Collections.Concurrent;
using HotChocolate;
using HotChocolate.Types;
using Trax.Api.Auth;
using Trax.Effect.Attributes;
using Trax.Mediator.Services.TrustedExecution;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Test-only GraphQL probe that exposes <see cref="TraxCaller"/> state in the
/// response payload. Lives in the test assembly so it is never wired into
/// production hosts. The attack-vector E2E suite uses these fields to assert
/// what a server-side caller actually sees after the framework has processed
/// a deliberately hostile HTTP request.
///
/// <para>
/// Both query and mutation variants exist because some attacks target the
/// query path (introspection, GET-like reads), others the mutation path
/// (request bodies with crafted variables / extensions).
/// </para>
/// </summary>
public record TraxCallerProbeResult(
    bool IsAuthenticated,
    bool IsTrusted,
    string? PrincipalId,
    IReadOnlyList<string> PrincipalRoles
);
