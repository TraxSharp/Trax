namespace Trax.Api.GraphQL.Client;

internal sealed class IntrospectionDirective
{
    public string Name { get; set; } = "";
    public List<string>? Locations { get; set; }
    public List<IntrospectionInputValue>? Args { get; set; }
}
