using System.Numerics;
using MKWSatellite.Formats.Course;

namespace MKWSatellite.Formats.Tests;

public class CourseMathTests
{
    /// <summary>
    /// The game's rail parser (RailGraph_ParseRail, 1.8) derives a point's up vector from Euler angles as
    /// (sx*sy*cz - cx*sz, cx*cz + sx*sy*sz, sx*cy). Our matrix must produce the same local +Y.
    /// </summary>
    [Theory]
    [InlineData(0.3f, -1.06f, 0.1f)]
    [InlineData(-3.142f, 0.504f, 3.142f)]
    [InlineData(0.0f, 1.5708f, -0.7f)]
    public void EulerXyz_UpVectorMatchesGameFormula(float x, float y, float z)
    {
        var up = Vector3.TransformNormal(Vector3.UnitY, CourseMath.EulerXyz(new Vector3(x, y, z)));
        float sx = MathF.Sin(x), cx = MathF.Cos(x), sy = MathF.Sin(y), cy = MathF.Cos(y), sz = MathF.Sin(z), cz = MathF.Cos(z);
        var expected = new Vector3(sx * sy * cz - cx * sz, cx * cz + sx * sy * sz, sx * cy);
        Assert.True(Vector3.Distance(up, expected) < 1e-4f, $"{up} vs {expected}");
    }

    /// <summary>Gate spans (offset - size) / 2 .. (offset + size) / 2: Airship Fortress node 12 (size 20x35, offset 0,30).</summary>
    [Fact]
    public void GateCorners_CentredOnHalfOffset()
    {
        var raw = new MKWSatellite.Formats.Byml.BymlDict(2)
        {
            ["RectSize"] = new MKWSatellite.Formats.Byml.BymlArray(2) { 20f, 35f },
            ["RectOffset"] = new MKWSatellite.Formats.Byml.BymlArray(2) { 0f, 30f },
        };
        var point = new BancRailPoint(0, new Vector3(0, 100, 0), Vector3.Zero, null, [], raw);
        var lap = new LapPoint(point, null!, 0);
        var g = lap.GateCorners();
        Assert.Equal(100 - 2.5f, g[0].Y, 3); // bottom: (30 - 35) / 2 below the node
        Assert.Equal(100 + 32.5f, g[2].Y, 3); // top: (30 + 35) / 2
        Assert.Equal(-10f, g[0].X, 3);
        Assert.Equal(10f, g[1].X, 3);
    }

    [Fact]
    public void Classifier_KnownTypes()
    {
        Assert.Equal(ObjectCategory.DashPanel, CourseObjectClassifier.Classify("CmnObj_DashBoardCinema000_A_00"));
        Assert.Equal(ObjectCategory.ItemBox, CourseObjectClassifier.Classify("CmnObj_ItemBox000_B_00"));
        Assert.Equal(ObjectCategory.RespawnArea, CourseObjectClassifier.Classify("SystemObj_RescueArea_00"));
        Assert.Equal(ObjectCategory.BoostRing, CourseObjectClassifier.Classify("CmnObj_AccelRing001_A_00"));
        Assert.Equal(ObjectCategory.BoostArea, CourseObjectClassifier.Classify("SystemObj_GlideAreaKiller_00"));
        Assert.Equal(ObjectCategory.DashPanel, CourseObjectClassifier.Classify("CmnObj_DashPlateRainbow000_K_00"));
        Assert.Equal(ObjectCategory.GlideRamp, CourseObjectClassifier.Classify("CmnObj_RainbowGlidePlate000_C_00"));
        // The developer label wins over the name when the database row is known.
        Assert.Equal(ObjectCategory.DashPanel, CourseObjectClassifier.Classify("CmnObj_Mystery_00", new ActorInfo("CmnObj_Mystery_00", "PropObj", "汎用ダッシュ板(テスト)")));
    }

    [Fact]
    public void CourseName_FromRaceFile()
    {
        Assert.Equal("Cinema", CourseLoader.CourseNameOf("Race_Cinema_A_00[Game03].bcett.byml.zs"));
        Assert.Equal("SFCMarioCircuit", CourseLoader.CourseNameOf("Race_SFCMarioCircuit_C_01[Game03].bcett.byml.zs"));
    }
}
