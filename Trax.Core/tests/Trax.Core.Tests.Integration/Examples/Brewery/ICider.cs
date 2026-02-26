using Trax.Core.Route;
using Trax.Core.Tests.Integration.Examples.Brewery.Steps.Bottle;
using Trax.Core.Tests.Integration.Examples.Brewery.Steps.Prepare;

namespace Trax.Core.Tests.Integration.Examples.Brewery;

public interface ICider : IRoute<Ingredients, List<GlassBottle>> { }
