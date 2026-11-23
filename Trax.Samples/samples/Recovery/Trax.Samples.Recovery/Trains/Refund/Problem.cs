using System.ComponentModel;
using Trax.Core.Decisions;
using Trax.Effect.Attributes;

namespace Trax.Samples.Recovery.Trains.Refund;

[Asks("What went wrong with this order?")]
public enum Problem
{
    [Description("It arrived damaged or broken.")]
    Damaged,

    [Description("It never arrived.")]
    Lost,

    [Description("It arrived as ordered, but the customer no longer wants it.")]
    Unwanted,
}
