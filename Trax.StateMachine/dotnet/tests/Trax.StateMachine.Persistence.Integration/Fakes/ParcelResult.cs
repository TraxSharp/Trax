using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>What a parcel tells its consignment when it ends: the label it was sealed with.</summary>
public sealed record ParcelResult
{
    public string Label { get; init; } = "";
}
