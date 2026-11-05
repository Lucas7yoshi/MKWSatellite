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

    [Fact]
    public void Classifier_KnownTypes()
    {
        Assert.Equal(ObjectCategory.DashPanel, CourseObjectClassifier.Classify("CmnObj_DashBoardCinema000_A_00"));
        Assert.Equal(ObjectCategory.ItemBox, CourseObjectClassifier.Classify("CmnObj_ItemBox000_B_00"));
        Assert.Equal(ObjectCategory.RespawnArea, CourseObjectClassifier.Classify("SystemObj_RescueArea_00"));
        Assert.Equal(ObjectCategory.BoostRing, CourseObjectClassifier.Classify("CmnObj_AccelRing001_A_00"));
        Assert.Equal(ObjectCategory.KillArea, CourseObjectClassifier.Classify("SystemObj_GlideAreaKiller_00"));
    }

    [Fact]
    public void CourseName_FromRaceFile()
    {
        Assert.Equal("Cinema", CourseLoader.CourseNameOf("Race_Cinema_A_00[Game03].bcett.byml.zs"));
        Assert.Equal("SFCMarioCircuit", CourseLoader.CourseNameOf("Race_SFCMarioCircuit_C_01[Game03].bcett.byml.zs"));
    }
}
