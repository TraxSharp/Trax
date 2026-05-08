namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

public sealed partial class PersistedOperationsBuilder
{
    /// <summary>
    /// Database connection string for the <c>trax.persisted_operation</c>
    /// table. Required.
    /// </summary>
    public PersistedOperationsBuilder UseDatabase(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException(
                "UseDatabase requires a connection string.",
                nameof(connectionString)
            );

        _databaseConnectionString = connectionString;
        return this;
    }
}
