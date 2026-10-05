using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Bookworm.Auth;
using Trax.Samples.Bookworm.Trains.Lending.BorrowBook.Junctions;

namespace Trax.Samples.Bookworm.Trains.Lending.BorrowBook;

/// <summary>Lends a book to the calling member. A write operation, gated to members.</summary>
/// <remarks>
/// <c>GraphQLOperation.Run</c> only: the junction acts as the request's caller, and a queued run,
/// executed later by a scheduler, would have none. This host has no scheduler to run it anyway.
/// </remarks>
[TraxAuthorize(Roles = BookwormRoles.Member)]
[TraxMutation(
    GraphQLOperation.Run,
    Namespace = GraphQLNamespaces.Lending,
    Description = "Borrows a book for the calling member"
)]
public class BorrowBookTrain : ServiceTrain<BorrowBookInput, BorrowBookOutput>, IBorrowBookTrain
{
    protected override Task<Either<Exception, BorrowBookOutput>> Junctions() =>
        Chain<BorrowBookJunction>().Resolve();
}
