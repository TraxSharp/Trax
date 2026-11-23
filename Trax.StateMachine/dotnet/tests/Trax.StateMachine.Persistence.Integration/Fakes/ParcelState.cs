using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

public enum ParcelState
{
    Packing,
    Packed,
    Damaged,
    Inspecting,
    Checked,
    CheckFailed,
    CheckCancelled,
}
