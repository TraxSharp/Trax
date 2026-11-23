using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

public class PinnedNote : Note
{
    public int Position { get; set; }
}
