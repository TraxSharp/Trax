using System.ComponentModel.DataAnnotations.Schema;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Data.Filters;
using HotChocolate.Data.Sorting;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

[TraxQueryModel(ExposeAs = typeof(IBookReference), BindFields = FieldBindingBehavior.Explicit)]
public class ConflictBook : IBookReference
{
    [Column("id")]
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public int Rating { get; set; }
}
