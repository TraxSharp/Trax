using Trax.Core.Extensions;

namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>
/// A field's type as written: a JSON type or a format (<c>a string</c>, <c>a uuid</c>), a named <c>Type:</c>
/// (<c>a Square</c>), <c>a list of &lt;type&gt;</c>, <c>a map of &lt;type&gt; to &lt;type&gt;</c>, or a bare
/// <c>a list</c> or <c>an object</c>.
/// </summary>
internal sealed record LxTypeRef(
    string Kind,
    string Name,
    LxTypeRef? Element = null,
    LxTypeRef? Key = null
)
{
    internal const string Simple = "simple";
    internal const string Named = "named";
    internal const string List = "list";
    internal const string Map = "map";

    public override string ToString() =>
        Kind switch
        {
            List when Element is not null => $"a list of {Element.Name}",
            Map => $"a map of {Key.NotNull().Name} to {Element.NotNull().Name}",
            _ => Name,
        };
}
