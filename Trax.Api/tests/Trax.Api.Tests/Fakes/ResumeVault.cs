using System.Globalization;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.Fakes;

/// <summary>Which wing holds the documents: an answer withheld from every record.</summary>
[TraxSensitive]
[Asks("Which wing of the vault holds the documents?")]
public enum ResumeVault
{
    EastWing,
    WestWing,
}
