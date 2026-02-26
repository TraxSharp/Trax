using Trax.Core.Step;
using Trax.Core.Tests.Integration.Examples.Brewery.Steps.Prepare;
using LanguageExt;

namespace Trax.Core.Tests.Integration.Examples.Brewery.Steps.Ferment;

public interface IFerment : IStep<BrewingJug, Unit> { }
