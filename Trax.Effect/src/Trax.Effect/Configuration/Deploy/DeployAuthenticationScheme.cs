using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Effect.Configuration.Deploy;

/// <summary>An authentication scheme the host registers.</summary>
public sealed class DeployAuthenticationScheme
{
    /// <summary>The scheme's name, as <c>AddAuthentication</c> registered it.</summary>
    public string Name { get; set; } = "";

    /// <summary>The handler's type name, such as <c>JwtBearerHandler</c> or <c>OpenIdConnectHandler</c>.</summary>
    public string Handler { get; set; } = "";
}
