using KedroSpaceflights.Data._03_Primary.Schemas;
using KedroSpaceflights.Data._04_Models.Schemas;

namespace KedroSpaceflights.Pipelines.DataScience.Nodes;

/// <summary>
/// Evaluates a trained linear regression model using test data and computes performance metrics.
/// </summary>
public static class EvaluateModelNode
{
    /// <summary>
    /// Creates a model evaluation function that computes R², MAE, and maximum error metrics,
    /// and also outputs the actual vs predicted values for visualization.
    /// </summary>
    /// <returns>
    /// A function that evaluates a <see cref="LinearRegressionModel"/> against test data
    /// and produces both <see cref="ModelMetrics"/> and <see cref="ModelPredictions"/>.
    /// </returns>
    public static Func<
        (LinearRegressionModel, IEnumerable<TestData>),
        Task<(ModelMetrics, IEnumerable<ModelPredictions>)>
    > Create()
    {
        return async (input) =>
        {
            var (model, testData) = input;
            var data = testData.ToList();

            if (data.Count == 0)
            {
                Console.WriteLine("No test data available for evaluation");
                return await Task.FromResult(
                    (
                        new ModelMetrics
                        {
                            R2Score = 0,
                            MeanAbsoluteError = 0,
                            MaxError = 0,
                        },
                        Enumerable.Empty<ModelPredictions>()
                    )
                );
            }

            // Make predictions
            var predictions = data.Select(d => Predict(model, d.Features)).ToList();
            var actuals = data.Select(d => (double)d.Label).ToList();

            // Calculate metrics
            var r2 = CalculateR2(actuals, predictions);
            var mae = CalculateMae(actuals, predictions);
            var maxError = CalculateMaxError(actuals, predictions);

            // Create prediction pairs for visualization
            var predictionPairs = actuals
                .Zip(
                    predictions,
                    (actual, predicted) =>
                        new ModelPredictions { Actual = actual, Predicted = predicted }
                )
                .ToList();

            return await Task.FromResult(
                (
                    new ModelMetrics
                    {
                        R2Score = (decimal)r2,
                        MeanAbsoluteError = (decimal)mae,
                        MaxError = (decimal)maxError,
                    },
                    (IEnumerable<ModelPredictions>)predictionPairs
                )
            );
        };
    }

    /// <summary>
    /// Predicts a price using the linear regression model and feature vector.
    /// </summary>
    /// <param name="model">The trained regression model.</param>
    /// <param name="features">The feature vector for prediction.</param>
    /// <returns>The predicted price value.</returns>
    /// <remarks>
    /// Excludes moon_clearance_complete to match training feature set.
    /// </remarks>
    private static double Predict(LinearRegressionModel model, FeatureVector features)
    {
        double prediction = model.Intercept;

        // Note: Excluding moon_clearance_complete to match training features
        var featureValues = new double[]
        {
            (double)features.Engines,
            (double)features.PassengerCapacity,
            (double)features.Crew,
            features.DCheckComplete ? 1.0 : 0.0,
            features.IataApproved ? 1.0 : 0.0,
            (double)features.CompanyRating,
            (double)features.ReviewScoresRating,
        };

        for (int i = 0; i < model.Coefficients.Length; i++)
        {
            prediction += model.Coefficients[i] * featureValues[i];
        }

        return prediction;
    }

    /// <summary>
    /// Calculates the R² (coefficient of determination) score.
    /// </summary>
    /// <param name="actuals">Actual target values.</param>
    /// <param name="predictions">Predicted values.</param>
    /// <returns>R² score where 1.0 indicates perfect prediction and 0.0 indicates prediction no better than the mean.</returns>
    private static double CalculateR2(List<double> actuals, List<double> predictions)
    {
        var mean = actuals.Average();
        var ssTotal = actuals.Sum(y => Math.Pow(y - mean, 2));
        var ssResidual = actuals.Zip(predictions, (a, p) => Math.Pow(a - p, 2)).Sum();
        return 1 - (ssResidual / ssTotal);
    }

    /// <summary>
    /// Calculates the Mean Absolute Error (MAE).
    /// </summary>
    /// <param name="actuals">Actual target values.</param>
    /// <param name="predictions">Predicted values.</param>
    /// <returns>The average absolute difference between actual and predicted values.</returns>
    private static double CalculateMae(List<double> actuals, List<double> predictions)
    {
        return actuals.Zip(predictions, (a, p) => Math.Abs(a - p)).Average();
    }

    /// <summary>
    /// Calculates the maximum absolute error across all predictions.
    /// </summary>
    /// <param name="actuals">Actual target values.</param>
    /// <param name="predictions">Predicted values.</param>
    /// <returns>The largest absolute difference between any actual and predicted value.</returns>
    private static double CalculateMaxError(List<double> actuals, List<double> predictions)
    {
        return actuals.Zip(predictions, (a, p) => Math.Abs(a - p)).Max();
    }
}
