using HotChocolate;
using HotChocolate.Types;

namespace Trax.Api.Tests.GraphQLClient.Fixtures;

public class NetsuiteQueries
{
    public Player? TypedCustomer(string email, [Service] TestPlayerStore store) =>
        store.All().FirstOrDefault(p => p.Name.Equals(email, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<Item> TypedCustomers() =>
        new[]
        {
            new Item("cust-1", "Acme", ItemCategory.Consumable),
            new Item("cust-2", "Beta", ItemCategory.Consumable),
        };
}
