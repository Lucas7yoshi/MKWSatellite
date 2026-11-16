using System.Numerics;

namespace MKWSatellite.Formats.Course;

/// <summary>
/// Splits the lap path into checkpoint sections for display: each lap node belongs to the last <c>CheckPoint</c> node at
/// or before it along its <c>LapPathGraph</c> rail (wrapping on closed rails). Nodes on a rail with no checkpoint before
/// them (branch rails) take the section of the nearest node that has one. A viewing aid, not game logic.
/// </summary>
public sealed class LapSections
{
    private readonly Dictionary<LapPoint, int> _section = new(ReferenceEqualityComparer.Instance);

    /// <summary>Highest checkpoint number on the course (sections are 0..Max), or -1 if it has none.</summary>
    public int Max { get; }

    public LapSections(CourseScene course)
    {
        Max = course.LapPoints.Select(l => l.CheckPoint is { } c ? (int)MathF.Round(c) : -1).DefaultIfEmpty(-1).Max();
        if (Max < 0) return;
        foreach (var rail in course.LapPoints.GroupBy(l => l.Rail, ReferenceEqualityComparer.Instance))
        {
            var pts = rail.OrderBy(l => l.Index).ToList();
            int? current = null;
            // Closed rails start in the section of their last checkpoint.
            if (pts[0].Rail.Rail.IsClosed && pts.LastOrDefault(l => l.CheckPoint is not null) is { CheckPoint: { } last })
                current = (int)MathF.Round(last);
            foreach (var l in pts)
            {
                if (l.CheckPoint is { } c) current = (int)MathF.Round(c);
                if (current is { } s) _section[l] = s;
            }
        }
        var known = course.LapPoints.Where(_section.ContainsKey).ToList();
        if (known.Count == 0) return;
        foreach (var l in course.LapPoints.Where(l => !_section.ContainsKey(l)).ToList())
            _section[l] = _section[known.MinBy(k => Vector3.DistanceSquared(k.Position, l.Position))!];
    }

    public int? Of(LapPoint p) => _section.TryGetValue(p, out var s) ? s : null;

    /// <summary>
    /// Whether section <paramref name="s"/> lies within <paramref name="before"/> sections before and
    /// <paramref name="after"/> after <paramref name="centre"/>, counting around the lap.
    /// </summary>
    public bool InWindow(int s, int centre, int before, int after) => InWindow(s, centre, before, after, Max + 1);

    /// <summary><see cref="InWindow(int,int,int,int)"/> for <paramref name="n"/> sections (0..n-1) around a lap.</summary>
    public static bool InWindow(int s, int centre, int before, int after, int n)
    {
        if (before + after + 1 >= n) return true;
        var d = ((s - centre) % n + n) % n; // 0..n-1 steps ahead
        return d <= after || n - d <= before;
    }
}
