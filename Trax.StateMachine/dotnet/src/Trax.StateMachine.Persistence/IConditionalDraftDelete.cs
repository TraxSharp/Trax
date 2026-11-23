using System.Runtime.InteropServices;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// A store that deletes a user's draft only while it is still the row and revision a caller read, and runs the
/// caller's follow-up writes in the same transaction. The draft service discards through it, so a draft written
/// between its check and its delete is kept, and the claims it releases go with the row or not at all.
/// </summary>
internal interface IConditionalDraftDelete
{
    /// <summary>
    /// Deletes <paramref name="userKey"/>'s draft of <paramref name="machine"/> under <paramref name="id"/> only while
    /// it is the row <paramref name="read"/> names (<see cref="StoredSnapshot.RowId"/>), at the revision and invoke
    /// token it was read with, then runs <paramref name="afterDelete"/> before committing. False, with nothing written
    /// and <paramref name="afterDelete"/> not run, when the row is gone or has changed since.
    /// </summary>
    /// <param name="userKey">The owning user's key.</param>
    /// <param name="machine">The machine id the draft belongs to.</param>
    /// <param name="id">The client-minted draft id.</param>
    /// <param name="read">The row as the caller read it; it must carry its row id and revision.</param>
    /// <param name="afterDelete">The writes that commit with the delete.</param>
    /// <remarks>Every write runs on a token nothing cancels: a delete that has started finishes.</remarks>
    Task<bool> DeleteAsRead(
        string userKey,
        string machine,
        Guid id,
        StoredSnapshot read,
        Func<Task> afterDelete
    );
}
