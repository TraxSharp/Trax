using System.Globalization;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.Fakes;

/// <summary>Queued, and so resumed, only by a caller in the <c>Resumer</c> role.</summary>
[TraxAuthorize(Roles = "Resumer")]
public class GuardedResumableTrain : ResumableTrainBase, IGuardedResumableTrain;
