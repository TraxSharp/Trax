using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Trax.Effect.Attributes;

namespace Trax.Samples.PersistedOperations.Models;

/// <summary>
/// A user-owned note. Exposed as a paginated/filterable GraphQL query via
/// <see cref="TraxQueryModelAttribute"/> and gated behind the <c>user</c>
/// role via <see cref="TraxAuthorizeAttribute"/>.
/// </summary>
/// <remarks>
/// Wiring <c>[TraxAuthorize]</c> here is what flips the <c>hasGated</c>
/// branch in <c>GraphQLServiceExtensions</c>, which in turn calls
/// <c>AddAuthorization()</c> on the GraphQL builder. That is the only
/// reason this model exists in the persisted-operations sample: it shows
/// that persisted-operation upload still works against a schema that
/// carries the <c>@authorize</c> directive, which used to crash
/// <c>UpsertAsync</c> with <c>MissingStateException</c> before
/// <c>HotChocolateSchemaValidator</c> seeded the authorization handler
/// into the validator's context data.
/// </remarks>
[TraxQueryModel(
    Namespace = GraphQLNamespaces.Notes,
    Description = "User notes, gated behind the 'user' role"
)]
[TraxAuthorize(Roles = "user")]
[Table("user_notes", Schema = "notes")]
public class UserNote
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("title")]
    public string Title { get; set; } = "";

    [Column("body")]
    public string Body { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
