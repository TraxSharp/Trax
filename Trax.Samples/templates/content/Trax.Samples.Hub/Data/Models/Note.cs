using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Trax.Effect.Attributes;

namespace Trax.Samples.Hub.Data.Models;

/// <summary>
/// A sample entity, automatically exposed as a paginated, filterable, sortable GraphQL query via
/// [TraxQueryModel]. Replace it with your own domain entities.
/// </summary>
[TraxQueryModel(Namespace = "app", Description = "Notes")]
[Table("notes")]
public class Note
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("text")]
    public string Text { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
