using Trax.Api.Auth;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Auth.Trains;

public record WhoAmIOutput(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Roles,
    string? PrincipalType
);
