using MKWSatellite.Formats.Phive;

namespace MKWSatellite.Formats.Tests;

public class PerformanceSurfaceTests
{
    // SubMaterialCollection order in PhiveConfig (1.8).
    private static readonly string[] Subs =
    [
        "Undefined", "Vehicle", "Object", "Water", "Asphalt", "Concrete", "Stone", "Wood", "Kusa", "Soil", "Sand", "Snow",
        "Ice", "Iron", "Kanaami", "Glass", "Carpet", "Cloud", "Rainbow", "AirWall", "Mud", "Lava", "GhostSwamp",
        "PetalViolet", "PetalOrange", "Leaf", "Vinyl", "Plastic", "Biscuit", "Candy", "ChocoChip", "FlowerRed",
        "FlowerWhite", "Bone",
    ];

    private static int Sub(string name) => Array.IndexOf(Subs, name);

    [Theory]
    [InlineData("Asphalt"), InlineData("Concrete"), InlineData("Stone"), InlineData("Wood"), InlineData("Iron"),
     InlineData("Kanaami"), InlineData("Glass"), InlineData("Carpet"), InlineData("Rainbow"), InlineData("Vinyl"),
     InlineData("Plastic"), InlineData("Biscuit"), InlineData("Candy"), InlineData("ChocoChip"), InlineData("Bone")]
    public void HardSurfacesAreTarmac(string sub) => Assert.Equal(PerformanceSurface.Tarmac, PerformanceSurfaces.Of("Road", Sub(sub)));

    [Theory]
    [InlineData("Kusa"), InlineData("Soil"), InlineData("Sand"), InlineData("Snow"), InlineData("Ice"), InlineData("Cloud"),
     InlineData("Mud"), InlineData("Leaf"), InlineData("FlowerRed"), InlineData("Undefined")]
    public void OtherSurfacesAreGravel(string sub) => Assert.Equal(PerformanceSurface.Gravel, PerformanceSurfaces.Of("Road", Sub(sub)));

    [Fact]
    public void MaterialRulesComeBeforeSurface()
    {
        Assert.Equal(PerformanceSurface.Dirt, PerformanceSurfaces.Of("Dirt", Sub("Asphalt")));
        Assert.Equal(PerformanceSurface.Dirt, PerformanceSurfaces.Of("LowDirt", Sub("Kusa")));
        Assert.Equal(PerformanceSurface.Water, PerformanceSurfaces.Of("Water", Sub("Water")));
        // Slip, Dash and the like go by surface like Road.
        Assert.Equal(PerformanceSurface.Gravel, PerformanceSurfaces.Of("Slip", Sub("Snow")));
        Assert.Equal(PerformanceSurface.Tarmac, PerformanceSurfaces.Of("Dash", Sub("Asphalt")));
        Assert.Equal(PerformanceSurface.None, PerformanceSurfaces.Of("Wall", Sub("Asphalt")));
    }
}
