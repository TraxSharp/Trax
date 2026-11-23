using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Test entity exposed via <see cref="TraxQueryModelAttribute"/>. Lives in
/// the isolated <c>test_auth</c> schema so it can coexist with the sample
/// tables without stepping on them.
/// </summary>
[TraxAllowAnonymous]
[TraxQueryModel(Namespace = "library", Description = "Test books for E2E auth coverage.")]
[Table("book_records", Schema = "test_auth")]
public class BookRecord
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("title")]
    public string Title { get; set; } = "";

    [Column("author")]
    public string Author { get; set; } = "";

    [Column("rating")]
    public int Rating { get; set; }
}
