// What the examples under sdk-reference/graphql-client/ take as given: the client namespaces and
// a request type to run. The sample types live in their own namespace, so a page that declares
// its own GetPlayerRequest or PlayerProfile (in the global namespace) uses its own.
global using GraphQLClientSamples;
global using Trax.Api.GraphQL.Client;
global using Trax.Api.GraphQL.Client.Trax;
global using Trax.Api.GraphQL.Client.Typed;

namespace GraphQLClientSamples
{
    public sealed record PlayerProfile(string Id, string Name, int Level);

    public sealed class GetPlayerRequest : IGraphQLClientRequest<PlayerProfile>
    {
        public required string Id { get; init; }

        public string Query =>
            "query GetPlayer($id: String!) { player(id: $id) { id name level } }";

        public object? Variables => new { id = Id };
    }

    public sealed record Invoice(string Id, decimal Total);

    [GraphQLClient("billing")]
    public sealed class GetInvoiceRequest : IGraphQLClientRequest<Invoice>
    {
        public required string Id { get; init; }

        public string Query => "query GetInvoice($id: ID!) { invoice(id: $id) { id total } }";

        public object? Variables => new { id = Id };
    }

    public sealed record Shipment(string Id, string Status);

    [GraphQLClient("shipping")]
    public sealed class GetShipmentRequest : IGraphQLClientRequest<Shipment>
    {
        public required string OrderId { get; init; }

        public string Query =>
            "query GetShipment($orderId: ID!) { shipment(orderId: $orderId) { id status } }";

        public object? Variables => new { orderId = OrderId };
    }

    /// <summary>Adds a bearer token to every request.</summary>
    public sealed class AuthHeaderHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            request.Headers.Authorization = new("Bearer", "token");
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>Stands in for the server's own schema configuration helper.</summary>
    public static class PlayerSchema
    {
        public static void Configure(
            HotChocolate.Execution.Configuration.IRequestExecutorBuilder graphql
        ) =>
            graphql.AddQueryType(d =>
                d.Name("Query")
                    .Field("ping")
                    .Type<HotChocolate.Types.StringType>()
                    .Resolve(_ => new ValueTask<object?>("pong"))
            );
    }
}
