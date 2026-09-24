namespace Trax.Core.Exceptions;

/// <summary>
/// Thrown when a train's chain declaration reads per-execution state.
/// </summary>
/// <remarks>
/// A chain is a declaration, not a step of the work. It is read once at startup, with no input
/// and no metadata, so that every train can be checked before the host serves traffic. Reading
/// the input while declaring the chain would make the chain depend on the value being processed,
/// which means there is no single chain to check: the one verified at startup need not be the
/// one that runs.
///
/// Move the work into a junction. A junction receives the input as its argument and is the place
/// per-execution decisions belong.
/// </remarks>
public sealed class ChainDeclarationException(string trainName, string member)
    : Exception(
        $"{trainName} read '{member}' while declaring its chain. A chain is a declaration and "
            + "cannot depend on the value being processed, because a chain that varies by input "
            + $"cannot be verified at startup. Move the work that needs '{member}' into a junction, "
            + "which receives the input as its argument."
    );
