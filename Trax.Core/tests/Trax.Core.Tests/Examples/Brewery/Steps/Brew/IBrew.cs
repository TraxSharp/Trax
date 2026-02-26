using Trax.Core.Step;
using Trax.Core.Tests.Examples.Brewery.Steps.Prepare;
using LanguageExt;

namespace Trax.Core.Tests.Examples.Brewery.Steps.Brew;

public interface IBrew : IStep<BrewingJug, Unit> { }
