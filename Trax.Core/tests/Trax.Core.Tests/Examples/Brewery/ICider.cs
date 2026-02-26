using Trax.Core.Route;
using Trax.Core.Tests.Examples.Brewery.Steps.Bottle;
using Trax.Core.Tests.Examples.Brewery.Steps.Prepare;

namespace Trax.Core.Tests.Examples.Brewery;

public interface ICider : IRoute<Ingredients, List<GlassBottle>> { }
