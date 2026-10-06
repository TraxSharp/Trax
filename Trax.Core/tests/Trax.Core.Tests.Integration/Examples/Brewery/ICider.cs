using Trax.Core.Route;
using Trax.Core.Tests.Integration.Examples.Brewery.Junctions.Bottle;
using Trax.Core.Tests.Integration.Examples.Brewery.Junctions.Prepare;

namespace Trax.Core.Tests.Integration.Examples.Brewery;

public interface ICider : IRoute<Ingredients, List<GlassBottle>> { }
