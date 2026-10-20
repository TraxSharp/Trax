namespace Shopfront.Trains.FulfilOrder.Models;

// The checkpoint stores it as JSON and reads it back, so it is sealed: a derived label would come
// back as this type.
public sealed record ShippingLabel(string TrackingNumber, decimal Price);
