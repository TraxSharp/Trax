namespace Shopfront.Fraud;

public record OrderRisk(string CustomerId, decimal Total, bool FirstOrder);
