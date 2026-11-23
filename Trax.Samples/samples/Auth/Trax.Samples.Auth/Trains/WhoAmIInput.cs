using Trax.Api.Auth;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Auth.Trains;

/// <summary>Empty on purpose: a query train needs its own input type, and the field then takes no argument.</summary>
public record WhoAmIInput;
