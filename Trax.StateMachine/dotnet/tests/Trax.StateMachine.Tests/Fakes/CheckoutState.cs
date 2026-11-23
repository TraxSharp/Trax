using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Tests.Fakes;

public enum CheckoutState
{
    Cart,
    Review,
    Paid,
}
