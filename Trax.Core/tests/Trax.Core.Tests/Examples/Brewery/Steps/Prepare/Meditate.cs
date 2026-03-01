using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Exceptions;
using Trax.Core.Step;
using static LanguageExt.Prelude;

namespace Trax.Core.Tests.Examples.Brewery.Steps.Prepare;

internal class Meditate : Step<Unit, Unit>
{
    public override async Task<Unit> Run(Unit input)
    {
        // You silently consider what you should brew
        return unit;
    }
}
