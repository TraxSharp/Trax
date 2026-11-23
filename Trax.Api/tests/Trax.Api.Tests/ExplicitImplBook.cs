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

[TraxQueryModel(ExposeAs = typeof(IBookExplicit))]
public class ExplicitImplBook : IBookExplicit
{
    public int Id { get; set; }

    // Explicit interface impl — no public `SecretField` property exists on
    // ExplicitImplBook, so the GraphQL field can't be named.
    string IBookExplicit.SecretField => "secret";
}
