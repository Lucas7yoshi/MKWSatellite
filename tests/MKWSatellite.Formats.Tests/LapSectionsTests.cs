using MKWSatellite.Formats.Course;

namespace MKWSatellite.Formats.Tests;

public class LapSectionsTests
{
    [Theory]
    [InlineData(5, 5, true)]
    [InlineData(4, 5, true)]   // one before
    [InlineData(7, 5, true)]   // two after
    [InlineData(3, 5, false)]
    [InlineData(8, 5, false)]
    [InlineData(9, 0, true)]   // wraps: section 9 is one before 0 on a 10-section lap
    [InlineData(2, 0, true)]
    [InlineData(3, 0, false)]
    [InlineData(8, 0, false)]
    public void WindowCountsAroundTheLap(int section, int centre, bool shown) =>
        Assert.Equal(shown, LapSections.InWindow(section, centre, before: 1, after: 2, n: 10));

    [Fact]
    public void WindowCoveringTheLapShowsEverything() =>
        Assert.All(Enumerable.Range(0, 4), s => Assert.True(LapSections.InWindow(s, 0, 2, 2, 4)));
}
