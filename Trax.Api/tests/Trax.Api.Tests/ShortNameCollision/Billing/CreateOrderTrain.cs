using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.TypeModules;
using Trax.Api.Tests.GraphQLClient.Fixtures;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests.ShortNameCollision.Billing;

[TraxAllowAnonymous]
[TraxMutation(GraphQLOperation.Run, Name = "BillingCreateOrder", Namespace = "shortNameCollision")]
public class CreateOrderTrain
    : ServiceTrain<BillingOrderInput, BillingOrderOutput>,
        ICreateOrderTrain
{
    protected override Task<Either<Exception, BillingOrderOutput>> Junctions() =>
        Chain<CreateOrderJunction>().Resolve();
}
