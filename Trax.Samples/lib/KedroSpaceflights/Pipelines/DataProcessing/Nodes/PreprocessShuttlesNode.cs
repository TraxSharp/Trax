using KedroSpaceflights.Data._01_Raw.Schemas;
using KedroSpaceflights.Data._02_Intermediate.Schemas;

namespace KedroSpaceflights.Pipelines.DataProcessing.Nodes;

/// <summary>
/// Preprocesses raw shuttle data by parsing numeric fields, boolean flags, and currency values.
/// </summary>
public static class PreprocessShuttlesNode
{
    /// <summary>
    /// Creates a preprocessing function that transforms raw shuttle records into strongly-typed records.
    /// </summary>
    /// <returns>
    /// A function that converts <see cref="ShuttleSchema"/> records to <see cref="PreprocessedShuttleSchema"/> records.
    /// Records with invalid numeric fields or currency values are filtered out.
    /// </returns>
    public static Func<
        IEnumerable<ShuttleSchema>,
        Task<IEnumerable<PreprocessedShuttleSchema>>
    > Create()
    {
        return async (input) =>
        {
            var processed = input
                .Select(raw => Parse(raw))
                .Where(item => item != null)
                .Cast<PreprocessedShuttleSchema>();

            return await Task.FromResult(processed);
        };
    }

    /// <summary>
    /// Parses a raw shuttle record into a preprocessed record with strongly-typed fields.
    /// </summary>
    /// <param name="raw">The raw shuttle record to parse.</param>
    /// <returns>
    /// A <see cref="PreprocessedShuttleSchema"/> if all fields parse successfully; otherwise, <c>null</c>.
    /// </returns>
    private static PreprocessedShuttleSchema? Parse(ShuttleSchema raw)
    {
        // Parse boolean fields
        bool dCheckComplete = raw.DCheckComplete.Trim().ToLowerInvariant() == "t";
        bool moonClearanceComplete = raw.MoonClearanceComplete.Trim().ToLowerInvariant() == "t";

        // Parse numeric fields
        if (!int.TryParse(raw.Engines, out var engines))
        {
            return null;
        }

        if (!int.TryParse(raw.PassengerCapacity, out var passengerCapacity))
        {
            return null;
        }

        if (!int.TryParse(raw.Crew, out var crew))
        {
            return null;
        }

        // Parse money string (e.g., "$1,234.56" -> 1234.56)
        if (!TryParseMoney(raw.Price, out var price))
        {
            return null;
        }

        return new PreprocessedShuttleSchema
        {
            Id = raw.Id,
            ShuttleType = raw.ShuttleType,
            CompanyId = raw.CompanyId,
            Engines = engines,
            PassengerCapacity = passengerCapacity,
            Crew = crew,
            Price = price,
            DCheckComplete = dCheckComplete,
            MoonClearanceComplete = moonClearanceComplete,
        };
    }

    /// <summary>
    /// Parses a currency string (e.g., "$1,234.56") to a decimal value (e.g., 1234.56).
    /// </summary>
    /// <param name="value">The currency string to parse. Expected format: optional "$", digits with optional commas, optional decimal point.</param>
    /// <param name="result">
    /// When this method returns, contains the decimal value if parsing succeeded,
    /// or zero if parsing failed.
    /// </param>
    /// <returns><c>true</c> if parsing succeeded; otherwise, <c>false</c>.</returns>
    private static bool TryParseMoney(string value, out decimal result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var cleaned = value.Replace("$", "").Replace(",", "").Trim();
        return decimal.TryParse(cleaned, out result);
    }
}
