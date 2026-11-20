using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth.Jwt;

namespace Trax.Samples.Shared.Auth;

/// <summary>What a page sends to sign one person in: a name and a role, nothing else.</summary>
public sealed record DemoSignInRequest(string Name, string Role);
