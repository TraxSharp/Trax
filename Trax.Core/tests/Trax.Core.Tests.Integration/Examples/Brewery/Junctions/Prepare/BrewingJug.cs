namespace Trax.Core.Tests.Integration.Examples.Brewery.Junctions.Prepare;

public class BrewingJug : IBrewingJug
{
    public required int Gallons { get; set; }

    public int Yeast { get; set; }

    public bool HasCinnamonSticks { get; set; }

    public bool IsFermented { get; set; }

    public bool IsBrewed { get; set; }

    public Ingredients Ingredients { get; set; } = null!;
}
