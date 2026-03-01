using Flowthru.Abstractions;

namespace KedroSpaceflights.Data._04_Models.Schemas;

/// <summary>
/// Represents a trained linear regression model with coefficients and feature mappings.
/// </summary>
[FlowthruSchema]
public partial record LinearRegressionModel
{
    /// <summary>
    /// Regression coefficients for each feature (excluding intercept).
    /// </summary>
    public double[] Coefficients { get; init; } = Array.Empty<double>();

    /// <summary>
    /// Intercept term (bias) of the regression model.
    /// </summary>
    public double Intercept { get; init; }

    /// <summary>
    /// Names of features corresponding to each coefficient.
    /// </summary>
    public string[] FeatureNames { get; init; } = Array.Empty<string>();
}
