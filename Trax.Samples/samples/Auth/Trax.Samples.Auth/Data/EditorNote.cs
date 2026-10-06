using System.ComponentModel.DataAnnotations.Schema;
using Trax.Effect.Attributes;

namespace Trax.Samples.Auth.Data;

/// <summary>
/// An internal note attached to an article. Not a query model of its own, but a query model
/// reaches it through <see cref="Article.EditorNote"/>, so it must declare a posture too.
/// </summary>
/// <remarks>
/// One attribute's role list is any of: an editor <b>or</b> an auditor may read it. Separate
/// attributes combine like ASP.NET Core's <c>[Authorize]</c>, each a requirement of its own, so two
/// stacked role attributes would require both roles.
/// </remarks>
[TraxAuthorize(Roles = Auth.AuthRoles.Editor + "," + Auth.AuthRoles.Auditor)]
[Table("editor_notes")]
public class EditorNote
{
    public long Id { get; set; }

    public string Text { get; set; } = "";
}
