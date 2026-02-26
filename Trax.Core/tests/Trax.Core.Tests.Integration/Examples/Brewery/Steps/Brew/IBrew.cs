using Trax.Core.Step;
using Trax.Core.Tests.Integration.Examples.Brewery.Steps.Prepare;
using LanguageExt;

namespace Trax.Core.Tests.Integration.Examples.Brewery.Steps.Brew;

public interface IBrew : IStep<BrewingJug, Unit> { }
