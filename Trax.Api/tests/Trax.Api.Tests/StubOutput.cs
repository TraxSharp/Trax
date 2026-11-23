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

public record StubOutput
{
    public string Result { get; init; } = "";
}
