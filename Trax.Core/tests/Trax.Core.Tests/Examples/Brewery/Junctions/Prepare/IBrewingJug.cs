namespace Trax.Core.Tests.Examples.Brewery.Junctions.Prepare;

public interface IBrewingJug
{
    int Gallons { get; set; }
    int Yeast { get; set; }
    bool HasCinnamonSticks { get; set; }
    bool IsFermented { get; set; }
    bool IsBrewed { get; set; }
    Ingredients Ingredients { get; set; }
}
