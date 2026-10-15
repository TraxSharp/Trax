using Trax.Api.GraphQL.TypeModules;

namespace Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;

public partial class TraxGraphQLBuilder
{
    internal HashSet<Type> MachineSpelledEnums { get; } = [];

    /// <summary>
    /// Lets a field outside the machines root (a train's, a query model's, a type extension's) carry
    /// <typeparamref name="TEnum"/> although a served machine holds it. A machine's enum is spelled on the wire as the
    /// server stores it, by its member names (<c>VeryHigh</c>), and GraphQL has one type per enum, so every other field
    /// that carries it changes from <c>VERY_HIGH</c> to <c>VeryHigh</c> too. The host refuses to start, naming the
    /// field, until each such enum is either given its own type on one side or named here. Trax's own enums
    /// (<c>TrainState</c> and the rest) are never shared: naming one here is refused, and a machine that holds one
    /// refuses the start whether or not it was named.
    /// </summary>
    /// <typeparam name="TEnum">The enum whose new spelling the host accepts everywhere.</typeparam>
    /// <exception cref="InvalidOperationException"><typeparamref name="TEnum"/> is declared in a Trax assembly.</exception>
    public TraxGraphQLBuilder ShareEnumWithMachines<TEnum>()
        where TEnum : struct, Enum
    {
        if (MachineEnums.IsTrax(typeof(TEnum)))
            throw new InvalidOperationException(
                $"ShareEnumWithMachines<{typeof(TEnum).Name}>() names {typeof(TEnum).FullName}, which is Trax's own. "
                    + MachineEnums.TraxEnumRemedy(typeof(TEnum))
            );
        MachineSpelledEnums.Add(typeof(TEnum));
        return this;
    }
}
