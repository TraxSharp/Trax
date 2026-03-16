using LanguageExt;
using Trax.Core.Junction;
using Trax.Core.Tests.Examples.Brewery.Junctions.Prepare;

namespace Trax.Core.Tests.Examples.Brewery.Junctions.Brew;

public interface IBrew : IJunction<BrewingJug, Unit> { }
