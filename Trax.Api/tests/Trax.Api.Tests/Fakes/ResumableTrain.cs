using System.Globalization;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.Fakes;

/// <summary>Resumable by anyone past the operations gate: it declares no requirement of its own.</summary>
public class ResumableTrain : ResumableTrainBase, IResumableTrain;
