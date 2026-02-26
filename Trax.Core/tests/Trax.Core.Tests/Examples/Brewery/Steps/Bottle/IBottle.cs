using Trax.Core.Step;
using Trax.Core.Tests.Examples.Brewery.Steps.Prepare;

namespace Trax.Core.Tests.Examples.Brewery.Steps.Bottle;

public interface IBottle : IStep<BrewingJug, List<GlassBottle>> { }
