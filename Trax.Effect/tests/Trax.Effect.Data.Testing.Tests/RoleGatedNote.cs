using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

[TraxQueryModel]
[TraxAuthorize(Roles = "subscriber")]
public class RoleGatedNote
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;
}
