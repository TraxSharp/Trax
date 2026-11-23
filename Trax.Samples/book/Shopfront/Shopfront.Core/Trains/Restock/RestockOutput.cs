using Shopfront.Trains.Restock.Models;

namespace Shopfront.Trains.Restock;

public record RestockOutput(IReadOnlyList<Delivery> Delivered);
