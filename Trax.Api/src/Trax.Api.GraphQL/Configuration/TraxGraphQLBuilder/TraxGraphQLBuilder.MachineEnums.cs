namespace Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;

public partial class TraxGraphQLBuilder
{
    internal HashSet<Type> MachineSpelledEnums { get; } = [];

    /// <summary>
    /// Lets a field outside the machines root (a train's, a query model's, a type extension's, Trax's own) carry
    /// <typeparamref name="TEnum"/> although a served machine holds it. A machine's enum is spelled on the wire as the
    /// server stores it, by its member names (<c>VeryHigh</c>), and GraphQL has one type per enum, so every other field
    /// that carries it changes from <c>VERY_HIGH</c> to <c>VeryHigh</c> too. The host refuses to start, naming the
    /// field, until each such enum is either given its own type on one side or named here.
    /// </summary>
    /// <typeparam name="TEnum">The enum whose new spelling the host accepts everywhere.</typeparam>
    public TraxGraphQLBuilder ShareEnumWithMachines<TEnum>()
        where TEnum : struct, Enum
    {
        MachineSpelledEnums.Add(typeof(TEnum));
        return this;
    }
}
