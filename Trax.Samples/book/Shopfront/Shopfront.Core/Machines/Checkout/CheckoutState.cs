namespace Shopfront.Machines.Checkout;

public enum CheckoutState
{
    Cart,
    Address,
    Payment,
    Placing,
    Placed,
    Failed,
    Shipping,
    Delivered,
    Abandoned,
}
