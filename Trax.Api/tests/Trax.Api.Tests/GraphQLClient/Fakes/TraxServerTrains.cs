using LanguageExt;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.GraphQLClient.Fakes;

// Trains decorated with [TraxQuery(Namespace = ...)] / [TraxMutation(Namespace = ...)]
// that get picked up by AddMediator's assembly scan. The TraxServerFixture spins up a
// real Trax GraphQL server backed by these, so end-to-end tests can prove the typed
// client's Path attribute pairs correctly with the envelope shape Trax itself emits.
//
// Namespace = "netsuiteClient" is deliberately specific so these don't collide with
// trains other test fixtures register against the same assembly scan.

public record LookupCustomerInput : IManifestProperties
{
    public required string Email { get; init; }
}

public record LookupCustomerOutput
{
    public required string Id { get; init; }
    public required string Email { get; init; }
    public required int CreditLimit { get; init; }
}

public interface ILookupCustomerTrain : IServiceTrain<LookupCustomerInput, LookupCustomerOutput>;

[TraxAllowAnonymous]
[TraxQuery(Namespace = "netsuiteClient", Description = "Looks up a customer by email.")]
public class LookupCustomerTrain
    : ServiceTrain<LookupCustomerInput, LookupCustomerOutput>,
        ILookupCustomerTrain
{
    protected override Task<Either<Exception, LookupCustomerOutput>> Junctions() =>
        Chain<LookupCustomerJunction>().Resolve();
}

internal sealed class LookupCustomerJunction : Junction<LookupCustomerInput, LookupCustomerOutput>
{
    public override Task<LookupCustomerOutput> Run(LookupCustomerInput input) =>
        Task.FromResult(
            new LookupCustomerOutput
            {
                Id = "cust-" + input.Email.GetHashCode().ToString("x"),
                Email = input.Email,
                CreditLimit = 50_000,
            }
        );
}

public record UpdateCreditLimitInput : IManifestProperties
{
    public required string CustomerId { get; init; }
    public required int NewLimit { get; init; }
}

public record UpdateCreditLimitOutput
{
    public required string CustomerId { get; init; }
    public required int OldLimit { get; init; }
    public required int NewLimit { get; init; }
}

public interface IUpdateCreditLimitTrain
    : IServiceTrain<UpdateCreditLimitInput, UpdateCreditLimitOutput>;

[TraxAllowAnonymous]
[TraxMutation(Namespace = "netsuiteClient", Description = "Updates a customer's credit limit.")]
public class UpdateCreditLimitTrain
    : ServiceTrain<UpdateCreditLimitInput, UpdateCreditLimitOutput>,
        IUpdateCreditLimitTrain
{
    protected override Task<Either<Exception, UpdateCreditLimitOutput>> Junctions() =>
        Chain<UpdateCreditLimitJunction>().Resolve();
}

internal sealed class UpdateCreditLimitJunction
    : Junction<UpdateCreditLimitInput, UpdateCreditLimitOutput>
{
    public override Task<UpdateCreditLimitOutput> Run(UpdateCreditLimitInput input) =>
        Task.FromResult(
            new UpdateCreditLimitOutput
            {
                CustomerId = input.CustomerId,
                OldLimit = 50_000,
                NewLimit = input.NewLimit,
            }
        );
}
