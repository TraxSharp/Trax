namespace Trax.Effect.Exceptions;

/// <summary>
/// Thrown by an effect provider's <c>SaveChanges</c> when its store refused the write because of
/// a value the row carries, rather than because the store is unavailable or the write conflicts:
/// a character the store cannot hold, or a value past one of its size limits.
/// </summary>
/// <remarks>
/// This is what lets a run record its state when its content cannot be stored. When the save of a
/// run's row fails with this exception, the train writes the row again with its input, output and
/// failure text replaced by fixed placeholders, so the state and end time still reach the store.
/// Any other failure of any provider propagates as it is and nothing is written again, because a
/// second write would replace a row another provider had already saved in full.
///
/// Trax's data providers throw it for the Postgres errors that name a value (SQLSTATE
/// <c>22001</c>, <c>22021</c>, <c>22P05</c> and <c>54000</c>) and for text the client cannot
/// encode. A custom provider that stores the run throws it, wrapping its own error, for the same
/// reason; a provider that does not store the run should not.
/// </remarks>
public class StoreRefusedContentException : Exception
{
    /// <summary>
    /// Creates the exception around the store's own error.
    /// </summary>
    /// <param name="innerException">The error the store raised.</param>
    public StoreRefusedContentException(Exception innerException)
        : base(
            "The store refused the write because of a value the row carries: "
                + innerException.Message,
            innerException
        ) { }
}
