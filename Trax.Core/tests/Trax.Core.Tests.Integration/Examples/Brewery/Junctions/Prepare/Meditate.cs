using LanguageExt;
using Trax.Core.Junction;
using static LanguageExt.Prelude;

namespace Trax.Core.Tests.Integration.Examples.Brewery.Junctions.Prepare;

internal class Meditate : Junction<Unit, Unit>
{
    public override async Task<Unit> Run(Unit input)
    {
        // You silently consider what you should brew
        return unit;
    }
}
