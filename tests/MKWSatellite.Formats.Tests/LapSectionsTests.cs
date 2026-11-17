using MKWSatellite.Formats.Course;

namespace MKWSatellite.Formats.Tests;

public class LapSectionsTests
{
    private static LapSections.Graph Build(int count, (int, int)[] edges, int start, int[] lapChecks, Dictionary<int, int> cps) =>
        LapSections.Build(count, edges, i => i == start, lapChecks.Contains, i => cps.TryGetValue(i, out var c) ? c : null, (a, b) => (a - b) * (a - b));

    private static (int, int)[] Chain(int from, int to) => Enumerable.Range(from, to - from).Select(i => (i, i + 1)).ToArray();

    /// <summary>A loop of 10 nodes, each a checkpoint 0..9 (start on 0), closed back to node 0.</summary>
    private static LapSections.Graph Loop() =>
        Build(10, [.. Chain(0, 9), (9, 0)], 0, [], Enumerable.Range(0, 10).ToDictionary(i => i, i => i));

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
        Assert.Equal(shown, Loop().InWindow(section, centre, before: 1, after: 2));

    [Fact]
    public void NumbersRepeatedAfterALapCheckAreSeparateSections()
    {
        // Approach 0-4 (CP 1 at 1, CP 2 at 3), lap check at 5, then a loop 5-9 with CP 1 at 6 and CP 2 at 8.
        var g = Build(10, [.. Chain(0, 9), (9, 5)], 0, [5], new() { [1] = 1, [3] = 2, [6] = 1, [8] = 2 });
        Assert.NotEqual(g.Of[1], g.Of[6]);
        Assert.NotEqual(g.Of[3], g.Of[8]);
        Assert.Equal(new LapSections.Section(1, 2), g.Sections[g.Of[9]]);
        // One after the approach's CP 2 is the lap check, not the lap's CP 1.
        Assert.True(g.InWindow(g.Of[5], g.Of[3], 0, 1));
        Assert.False(g.InWindow(g.Of[6], g.Of[3], 0, 1));
    }

    [Fact]
    public void BranchesWithTheSameNumberShareASection()
    {
        // 0 -> CP 1 at 1 -> splits into 2 and 3 (both CP 2) -> join at 4 (CP 3).
        var g = Build(5, [(0, 1), (1, 2), (1, 3), (2, 4), (3, 4)], 0, [], new() { [1] = 1, [2] = 2, [3] = 2, [4] = 3 });
        Assert.Equal(g.Of[2], g.Of[3]);
        Assert.True(g.InWindow(g.Of[4], g.Of[1], 0, 2));
    }

    [Fact]
    public void NodesWithoutACheckpointFollowThePath()
    {
        // A link rail 3-4 with no checkpoint of its own, entered from CP 1's stretch, belongs to CP 1.
        var g = Build(6, [(0, 1), (1, 2), (2, 3), (3, 4), (4, 5)], 0, [], new() { [1] = 1, [5] = 2 });
        Assert.Equal(g.Of[1], g.Of[3]);
        Assert.Equal(g.Of[1], g.Of[4]);
    }
}
