namespace Core.Import.Geometry;

public static class ViewportMath
{
    public static List<double[]> WorldFootprint(double centerPaperX, double centerPaperY, double width, double height, double customScale, double viewCenterX, double viewCenterY, double twistRadians = 0)
    {
        var halfW = width / 2.0;
        var halfH = height / 2.0;
        var paperCorners = new (double X, double Y)[]
        {
            (centerPaperX - halfW, centerPaperY - halfH),
            (centerPaperX + halfW, centerPaperY - halfH),
            (centerPaperX + halfW, centerPaperY + halfH),
            (centerPaperX - halfW, centerPaperY + halfH)
        };

        var result = new List<double[]>();
        var cos = Math.Cos(twistRadians);
        var sin = Math.Sin(twistRadians);
        foreach (var (px, py) in paperCorners)
        {
            var dx = (px - centerPaperX) / customScale;
            var dy = (py - centerPaperY) / customScale;
            // Apply twist about view center in DCS, then treat as WCS for plan (direction = +Z).
            var rx = dx * cos - dy * sin;
            var ry = dx * sin + dy * cos;
            result.Add([viewCenterX + rx, viewCenterY + ry]);
        }
        return result;
    }

    public static bool PointInPolygon(double x, double y, IReadOnlyList<double[]> polygon)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var xi = polygon[i][0]; var yi = polygon[i][1];
            var xj = polygon[j][0]; var yj = polygon[j][1];
            var intersect = ((yi > y) != (yj > y)) && (x < (xj - xi) * (y - yi) / (yj - yi + 0.0) + xi);
            if (intersect) inside = !inside;
        }
        return inside;
    }

    public static bool SegmentIntersectsPolygon(double x1, double y1, double x2, double y2, IReadOnlyList<double[]> polygon)
    {
        if (PointInPolygon(x1, y1, polygon) || PointInPolygon(x2, y2, polygon))
            return true;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            if (SegmentsIntersect(x1, y1, x2, y2, a[0], a[1], b[0], b[1]))
                return true;
        }
        return false;
    }

    private static bool SegmentsIntersect(double x1, double y1, double x2, double y2, double x3, double y3, double x4, double y4)
    {
        static double Cross(double ax, double ay, double bx, double by) => ax * by - ay * bx;
        var d1 = Cross(x4 - x3, y4 - y3, x1 - x3, y1 - y3);
        var d2 = Cross(x4 - x3, y4 - y3, x2 - x3, y2 - y3);
        var d3 = Cross(x2 - x1, y2 - y1, x3 - x1, y3 - y1);
        var d4 = Cross(x2 - x1, y2 - y1, x4 - x1, y4 - y1);
        return d1 * d2 < 0 && d3 * d4 < 0;
    }
}
