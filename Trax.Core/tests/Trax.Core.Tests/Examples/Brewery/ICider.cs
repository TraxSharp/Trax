using Trax.Core.Route;
using Trax.Core.Tests.Examples.Brewery.Junctions.Bottle;
using Trax.Core.Tests.Examples.Brewery.Junctions.Prepare;

namespace Trax.Core.Tests.Examples.Brewery;

public interface ICider : IRoute<Ingredients, List<GlassBottle>> { }
