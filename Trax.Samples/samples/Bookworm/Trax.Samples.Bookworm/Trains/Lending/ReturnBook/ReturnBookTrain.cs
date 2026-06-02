using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Bookworm.Auth;
using Trax.Samples.Bookworm.Trains.Lending.ReturnBook.Junctions;

namespace Trax.Samples.Bookworm.Trains.Lending.ReturnBook;

/// <summary>Marks a loan returned. A write operation, gated to authenticated members.</summary>
[TraxAuthorize(Roles = BookwormRoles.Member)]
[TraxMutation(Namespace = GraphQLNamespaces.Lending, Description = "Returns a borrowed book")]
public class ReturnBookTrain : ServiceTrain<ReturnBookInput, ReturnBookOutput>, IReturnBookTrain
{
    protected override Task<Either<Exception, ReturnBookOutput>> Junctions() =>
        Chain<ReturnBookJunction>().Resolve();
}
