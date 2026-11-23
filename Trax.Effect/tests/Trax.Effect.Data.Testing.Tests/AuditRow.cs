using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

public class AuditRow
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
}
