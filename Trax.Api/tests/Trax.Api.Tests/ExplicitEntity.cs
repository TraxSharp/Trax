using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using AwesomeAssertions;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

[TraxAllowAnonymous]
[TraxQueryModel(BindFields = FieldBindingBehavior.Explicit)]
public class ExplicitEntity
{
    [Column("id")]
    public int Id { get; set; }

    [Column("display_name")]
    public string DisplayName { get; set; } = "";

    [NotMapped]
    public string ComputedAlias => string.Create(CultureInfo.InvariantCulture, $"Entity-{Id}");

    public string NonColumnProp { get; set; } = "";

    public void AddToDbContext() { }
}
