using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Junction;

namespace Trax.Core.Tests.Examples.Brewery.Junctions.Prepare;

internal class Meditate : Junction<Unit, Unit>
{
    public override async Task<Unit> Run(Unit input)
    {
        // You silently consider what you should brew
        return Unit.Default;
    }
}
