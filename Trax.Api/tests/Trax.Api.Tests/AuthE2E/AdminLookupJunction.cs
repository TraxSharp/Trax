using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.AuthE2E;

internal sealed class AdminLookupJunction : Junction<AdminLookupInput, AdminLookupOutput>
{
    public override Task<AdminLookupOutput> Run(AdminLookupInput input) =>
        Task.FromResult(new AdminLookupOutput { Secret = $"classified:{input.Target}" });
}
