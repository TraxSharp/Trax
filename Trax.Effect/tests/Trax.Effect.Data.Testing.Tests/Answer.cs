using Microsoft.EntityFrameworkCore;
using Trax.Core.Extensions;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

public class Answer
{
    public int Id { get; set; }
    public int PollId { get; set; }
    public required Poll Poll { get; set; }
}
