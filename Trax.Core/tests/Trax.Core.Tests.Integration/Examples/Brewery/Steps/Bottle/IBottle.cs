using Trax.Core.Step;
using Trax.Core.Tests.Integration.Examples.Brewery.Steps.Prepare;

namespace Trax.Core.Tests.Integration.Examples.Brewery.Steps.Bottle;

public interface IBottle : IStep<BrewingJug, List<GlassBottle>> { }
