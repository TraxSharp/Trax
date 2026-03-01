namespace KedroSpaceflights.Data._03_Primary.Schemas;

/// <summary>
/// Training data pair (features + label)
/// </summary>
public record TrainingData
{
    public FeatureVector Features { get; init; } = null!;
    public decimal Label { get; init; } // Price
}

/// <summary>
/// Test data pair (features + label)
/// </summary>
public record TestData
{
    public FeatureVector Features { get; init; } = null!;
    public decimal Label { get; init; } // Price
}

/// <summary>
/// Feature vector for model training/testing
/// </summary>
public record FeatureVector
{
    public int Engines { get; init; }
    public int PassengerCapacity { get; init; }
    public int Crew { get; init; }
    public bool DCheckComplete { get; init; }
    public bool MoonClearanceComplete { get; init; }
    public bool IataApproved { get; init; }
    public decimal CompanyRating { get; init; }
    public decimal ReviewScoresRating { get; init; }
}
