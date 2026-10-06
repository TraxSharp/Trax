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

namespace Trax.Api.Tests
{
    /// <summary>
    /// Two trains whose interfaces share a short name (<c>ICreateOrderTrain</c>) in different
    /// namespaces. The canonical train name is the interface FullName; the short name is not
    /// unique, so nothing that routes a call may key on it.
    /// </summary>
    [TestFixture]
    public class TrainShortNameCollisionTests
    {
        /// <summary>
        /// Each train carries its own GraphQL <c>Name</c>, so the schema has two distinct fields.
        /// Selecting one must run that train.
        /// </summary>
        [Test]
        public async Task A_mutation_for_one_of_two_same_short_named_trains_runs_that_train()
        {
            using var fixture = new TraxServerFixture();
            using var client = fixture.CreateHttpClient();

            using var response = await client.PostAsJsonAsync(
                fixture.BaseAddress,
                new
                {
                    query = """
                    mutation {
                      dispatch { shortNameCollision {
                        billingCreateOrder(input: { sku: "a" }) { output { source } }
                      } }
                    }
                    """,
                }
            );
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            doc.RootElement.TryGetProperty("errors", out var errors)
                .Should()
                .BeFalse("the field names one train unambiguously, but got {0}", errors.ToString());
            doc.RootElement.GetProperty("data")
                .GetProperty("dispatch")
                .GetProperty("shortNameCollision")
                .GetProperty("billingCreateOrder")
                .GetProperty("output")
                .GetProperty("source")
                .GetString()
                .Should()
                .Be("billing");
        }

        /// <summary>
        /// Without a <c>Name</c> override both trains would claim the same field. The schema
        /// refuses to build, naming both trains, rather than picking a name for one of them.
        /// </summary>
        [Test]
        public async Task Two_same_short_named_trains_without_a_name_override_refuse_naming_both()
        {
            var registrations = new TrainDiscoveryService(ServiceCollectionWithBothTrains())
                .DiscoverTrains()
                .Select(r => WithoutGraphQLName(r))
                .ToList();

            registrations.Should().HaveCount(2);
            registrations
                .Select(r => r.ServiceTypeName)
                .Distinct()
                .Should()
                .ContainSingle("both interfaces are called ICreateOrderTrain");

            var services = new ServiceCollection();
            services.AddSingleton<ITrainDiscoveryService>(new StubDiscovery(registrations));
            services.AddSingleton<TrainTypeModule>();
            services
                .AddGraphQLServer("trax")
                .AddQueryType(d =>
                    d.Name("RootQuery").Field("_ping").Type<StringType>().Resolve("pong")
                )
                .AddType<RootMutation>()
                .AddTypeExtension(
                    new ObjectTypeExtension(d =>
                        d.Name("RootMutation").Field("_ping").Type<StringType>().Resolve("pong")
                    )
                )
                .AddTypeModule<TrainTypeModule>();

            var act = async () =>
                await services
                    .BuildServiceProvider()
                    .GetRequiredService<IRequestExecutorProvider>()
                    .GetExecutorAsync("trax");

            var thrown = await act.Should().ThrowAsync<Exception>();
            // HotChocolate wraps a type module's exception; ToString carries the inner ones.
            var messages = thrown.Which.ToString();
            messages
                .Should()
                .Contain(typeof(ShortNameCollision.Billing.ICreateOrderTrain).FullName);
            messages.Should().Contain(typeof(ShortNameCollision.Shop.ICreateOrderTrain).FullName);
            messages.Should().Contain("Set Name");
        }

        private static IServiceCollection ServiceCollectionWithBothTrains()
        {
            var services = new ServiceCollection();
            services.AddScoped<
                ShortNameCollision.Billing.ICreateOrderTrain,
                ShortNameCollision.Billing.CreateOrderTrain
            >();
            services.AddScoped<
                ShortNameCollision.Shop.ICreateOrderTrain,
                ShortNameCollision.Shop.CreateOrderTrain
            >();
            return services;
        }

        private static TrainRegistration WithoutGraphQLName(TrainRegistration r) =>
            new()
            {
                ServiceType = r.ServiceType,
                ImplementationType = r.ImplementationType,
                InputType = r.InputType,
                OutputType = r.OutputType,
                Lifetime = r.Lifetime,
                ServiceTypeName = r.ServiceTypeName,
                ImplementationTypeName = r.ImplementationTypeName,
                InputTypeName = r.InputTypeName,
                OutputTypeName = r.OutputTypeName,
                RequiredPolicies = r.RequiredPolicies,
                RequiredRoles = r.RequiredRoles,
                HasAuthorizeAttribute = r.HasAuthorizeAttribute,
                HasAllowAnonymousAttribute = r.HasAllowAnonymousAttribute,
                IsQuery = r.IsQuery,
                IsMutation = r.IsMutation,
                IsBroadcastEnabled = r.IsBroadcastEnabled,
                IsRemote = r.IsRemote,
                GraphQLName = null,
                GraphQLDescription = r.GraphQLDescription,
                GraphQLDeprecationReason = r.GraphQLDeprecationReason,
                GraphQLOperations = r.GraphQLOperations,
                GraphQLNamespace = r.GraphQLNamespace,
            };

        private sealed class StubDiscovery(IReadOnlyList<TrainRegistration> registrations)
            : ITrainDiscoveryService
        {
            public IReadOnlyList<TrainRegistration> DiscoverTrains() => registrations;
        }
    }
}

namespace Trax.Api.Tests.ShortNameCollision.Billing
{
    public record BillingOrderInput : IManifestProperties
    {
        public required string Sku { get; init; }
    }

    public record BillingOrderOutput
    {
        public required string Source { get; init; }
    }

    public interface ICreateOrderTrain : IServiceTrain<BillingOrderInput, BillingOrderOutput>;

    [TraxAllowAnonymous]
    [TraxMutation(
        GraphQLOperation.Run,
        Name = "BillingCreateOrder",
        Namespace = "shortNameCollision"
    )]
    public class CreateOrderTrain
        : ServiceTrain<BillingOrderInput, BillingOrderOutput>,
            ICreateOrderTrain
    {
        protected override Task<Either<Exception, BillingOrderOutput>> Junctions() =>
            Chain<CreateOrderJunction>().Resolve();
    }

    internal sealed class CreateOrderJunction : Junction<BillingOrderInput, BillingOrderOutput>
    {
        public override Task<BillingOrderOutput> Run(BillingOrderInput input) =>
            Task.FromResult(new BillingOrderOutput { Source = "billing" });
    }
}

namespace Trax.Api.Tests.ShortNameCollision.Shop
{
    public record ShopOrderInput : IManifestProperties
    {
        public required string Sku { get; init; }
    }

    public record ShopOrderOutput
    {
        public required string Source { get; init; }
    }

    public interface ICreateOrderTrain : IServiceTrain<ShopOrderInput, ShopOrderOutput>;

    [TraxAllowAnonymous]
    [TraxMutation(GraphQLOperation.Run, Name = "ShopCreateOrder", Namespace = "shortNameCollision")]
    public class CreateOrderTrain : ServiceTrain<ShopOrderInput, ShopOrderOutput>, ICreateOrderTrain
    {
        protected override Task<Either<Exception, ShopOrderOutput>> Junctions() =>
            Chain<CreateOrderJunction>().Resolve();
    }

    internal sealed class CreateOrderJunction : Junction<ShopOrderInput, ShopOrderOutput>
    {
        public override Task<ShopOrderOutput> Run(ShopOrderInput input) =>
            Task.FromResult(new ShopOrderOutput { Source = "shop" });
    }
}
