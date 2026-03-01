using LanguageExt;
using Trax.Core.Step;
using Trax.Core.Tests.Integration.Examples.Brewery.Steps.Prepare;

namespace Trax.Core.Tests.Integration.Examples.Brewery.Steps.Brew;

public interface IBrew : IStep<BrewingJug, Unit> { }
