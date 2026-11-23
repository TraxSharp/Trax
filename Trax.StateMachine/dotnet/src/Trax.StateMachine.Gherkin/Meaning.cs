using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>What one step means. A user machine's instance is named by its owner; a system machine's has none.</summary>
internal abstract record Meaning
{
    /// <summary><c>Ada is a customer</c>: names a person for the scenario.</summary>
    internal sealed record Named(string Name, string Actor) : Meaning;

    /// <summary><c>Ada has an order</c>: a person starts an instance of their own.</summary>
    internal sealed record Start(string Person, string Machine) : Meaning;

    /// <summary><c>Ada places her order</c>: a person fires a trigger.</summary>
    internal sealed record Act(
        string Person,
        string Machine,
        string? Owner,
        string Trigger,
        JsonObject? Input
    ) : Meaning;

    /// <summary><c>Ada's order starts baking</c>, <c>the shop opens</c>: a trigger no person fires.</summary>
    internal sealed record Happens(
        string Machine,
        string? Owner,
        string Trigger,
        JsonObject? Input,
        string? Key = null
    ) : Meaning;

    /// <summary><c>Ada's order is placed</c>, or with <see cref="Still"/>, <c>is still a draft</c>.</summary>
    internal sealed record InState(
        string Machine,
        string? Owner,
        string State,
        bool Still,
        string? Key = null
    ) : Meaning;

    /// <summary><c>Ada's order has nothing in it</c>: a guard holds or fails.</summary>
    internal sealed record Guard(
        string Machine,
        string? Owner,
        string Name,
        bool Holds,
        string? Key = null
    ) : Meaning;

    /// <summary><c>the payment goes through</c>: an invoked train's outcome.</summary>
    internal sealed record Outcome(string Machine, string State, string Phrase) : Meaning;

    /// <summary><c>Ada is told "Add something first."</c></summary>
    internal sealed record Told(string Person, string Message) : Meaning;

    /// <summary><c>Ada is not allowed</c></summary>
    internal sealed record Denied(string Person) : Meaning;

    /// <summary><c>5 minutes pass</c></summary>
    internal sealed record Passes(TimeSpan Time) : Meaning;

    /// <summary><c>Ada's orders show status "In the oven"</c>: a person reads one field of a view.</summary>
    internal sealed record ViewShows(string Person, string View, string Field, string Value)
        : Meaning;
}
