using Trax.Core.Junction;
using Trax.Core.Tests.Examples.Brewery.Junctions.Prepare;

namespace Trax.Core.Tests.Examples.Brewery.Junctions.Bottle;

public interface IBottle : IJunction<BrewingJug, List<GlassBottle>> { }
