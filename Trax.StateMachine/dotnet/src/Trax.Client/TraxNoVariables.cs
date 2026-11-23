namespace Trax.Client;

/// <summary>The variables of a document that declares none. Sent as <c>{}</c>.</summary>
public sealed record TraxNoVariables
{
    /// <summary>The one value.</summary>
    public static readonly TraxNoVariables Instance = new();

    private TraxNoVariables() { }
}
