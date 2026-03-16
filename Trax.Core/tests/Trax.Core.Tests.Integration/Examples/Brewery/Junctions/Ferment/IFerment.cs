using LanguageExt;
using Trax.Core.Junction;
using Trax.Core.Tests.Integration.Examples.Brewery.Junctions.Prepare;

namespace Trax.Core.Tests.Integration.Examples.Brewery.Junctions.Ferment;

public interface IFerment : IJunction<BrewingJug, Unit> { }
