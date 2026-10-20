using Shopfront.Data;
using Shopfront.Machines;
using Trax.Api.Auth;

namespace Shopfront.Auth;

// The signed-in caller, or, in a run a checkout invoked, the checkout's owner.
public sealed class TraxShopfrontCaller(TraxCaller caller, IServiceProvider services)
    : IShopfrontCaller
{
    public string? CustomerId => caller.Principal?.Id ?? InvokedRuns.Current(services)?.UserKey;
    public bool IsStaff => caller.Principal?.Roles.Contains(Roles.Staff) == true;
}
