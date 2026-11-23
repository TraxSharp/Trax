using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

[TraxQueryModel]
[TraxAuthorize(Roles = "admin")]
public class NoteView
{
    public int Id { get; set; }
    public int AccountId { get; set; }
}
