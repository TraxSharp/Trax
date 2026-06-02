using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Bookworm.Auth;
using Trax.Samples.Bookworm.Trains.Lending.BorrowBook.Junctions;

namespace Trax.Samples.Bookworm.Trains.Lending.BorrowBook;

/// <summary>Records a member borrowing a book. A write operation, gated to authenticated members.</summary>
[TraxAuthorize(Roles = BookwormRoles.Member)]
[TraxMutation(Namespace = GraphQLNamespaces.Lending, Description = "Borrows a book for a member")]
public class BorrowBookTrain : ServiceTrain<BorrowBookInput, BorrowBookOutput>, IBorrowBookTrain
{
    protected override Task<Either<Exception, BorrowBookOutput>> Junctions() =>
        Chain<BorrowBookJunction>().Resolve();
}
