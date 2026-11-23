using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trax.Cli.Deploy;

/// <summary>The sign-in a stack creates.</summary>
internal sealed class DeployAuth
{
    public const string Cognito = "cognito";

    public string Kind { get; set; } = Cognito;

    /// <summary>The setting the hosts read the issuer from.</summary>
    public string AuthorityKey { get; set; } = "Auth__Authority";

    /// <summary>The setting the hosts read the audience (the app client's id) from.</summary>
    public string AudienceKey { get; set; } = "Auth__Audience";
}
