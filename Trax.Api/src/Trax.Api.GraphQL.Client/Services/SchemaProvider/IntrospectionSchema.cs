namespace Trax.Api.GraphQL.Client;

internal sealed class IntrospectionSchema
{
    public IntrospectionTypeRef QueryType { get; set; } = null!;
    public IntrospectionTypeRef? MutationType { get; set; }
    public IntrospectionTypeRef? SubscriptionType { get; set; }
    public List<IntrospectionType> Types { get; set; } = new();
    public List<IntrospectionDirective>? Directives { get; set; }
}
