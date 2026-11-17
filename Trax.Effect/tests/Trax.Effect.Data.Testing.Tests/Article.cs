using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

public class Article
{
    public int Id { get; set; }
    public DateTime? DeletedAt { get; set; }
}
