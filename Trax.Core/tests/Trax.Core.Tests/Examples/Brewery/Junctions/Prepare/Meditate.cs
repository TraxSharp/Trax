using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using static LanguageExt.Prelude;

namespace Trax.Core.Tests.Examples.Brewery.Junctions.Prepare;

internal class Meditate : Junction<Unit, Unit>
{
    public override async Task<Unit> Run(Unit input)
    {
        // You silently consider what you should brew
        return unit;
    }
}
