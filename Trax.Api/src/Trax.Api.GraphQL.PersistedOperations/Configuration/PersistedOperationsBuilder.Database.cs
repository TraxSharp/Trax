namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

public sealed partial class PersistedOperationsBuilder
{
    /// <summary>
    /// Not used. Persisted operations read and write <c>trax.persisted_operation</c> through the
    /// Trax data context that <c>AddEffects(e =&gt; e.UsePostgres(...))</c> registers, so this
    /// connection string never chose a database. Kept so hosts that call it compile.
    /// </summary>
    [Obsolete(
        "Not used: persisted operations read and write through the Trax data context registered by "
            + "AddEffects(e => e.UsePostgres(...)). Remove the call."
    )]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public PersistedOperationsBuilder UseDatabase(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException(
                "UseDatabase requires a connection string.",
                nameof(connectionString)
            );

        return this;
    }
}
