namespace Shieldsmith.Diagrams.Rendering;

/// <summary>
/// Turns an edge's polyline into a path with tight rounded corners, shared by
/// the SVG and PNG renderers so both draw the same shape.
///
/// The obvious approach, curving from each corner to the midpoint of the next
/// segment, rounds by half the segment length. On two edges leaving the same
/// point and turning opposite ways that produced a lens shaped bulge under the
/// node rather than two lines going their separate ways. Capping the radius
/// keeps the corner local to the corner.
/// </summary>
public static class EdgeGeometry
{
    /// <summary>Corner radius, before it is capped by the segments meeting there.</summary>
    public const double CornerRadius = 10;

    /// <summary>
    /// One step of a path. <see cref="Control"/> is null for a straight line and
    /// set for a quadratic curve rounding a corner.
    /// </summary>
    public readonly record struct PathStep(PointD To, PointD? Control);

    /// <summary>
    /// The first step is the start point. Returns an empty list for a degenerate
    /// polyline, which callers treat as nothing to draw.
    /// </summary>
    public static List<PathStep> Rounded(IReadOnlyList<PointD> points, double radius = CornerRadius)
    {
        var steps = new List<PathStep>();
        if (points.Count < 2) return steps;

        steps.Add(new PathStep(points[0], null));

        for (var i = 1; i < points.Count - 1; i++)
        {
            var previous = points[i - 1];
            var corner = points[i];
            var next = points[i + 1];

            var incoming = Length(previous, corner);
            var outgoing = Length(corner, next);
            // Never round by more than half of either segment, or consecutive
            // corners would eat into each other and the line would drift.
            var r = Math.Min(radius, Math.Min(incoming, outgoing) / 2);
            if (r < 0.5)
            {
                steps.Add(new PathStep(corner, null));
                continue;
            }

            steps.Add(new PathStep(Along(corner, previous, r), null));
            steps.Add(new PathStep(Along(corner, next, r), corner));
        }

        steps.Add(new PathStep(points[^1], null));
        return steps;
    }

    private static double Length(PointD a, PointD b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>The point <paramref name="distance"/> from <paramref name="from"/> towards <paramref name="towards"/>.</summary>
    private static PointD Along(PointD from, PointD towards, double distance)
    {
        var length = Length(from, towards);
        if (length < 0.0001) return from;
        var t = distance / length;
        return new PointD(from.X + (towards.X - from.X) * t, from.Y + (towards.Y - from.Y) * t);
    }
}
