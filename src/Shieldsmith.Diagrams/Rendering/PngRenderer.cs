using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Shieldsmith.Diagrams.Layout;

namespace Shieldsmith.Diagrams.Rendering;

/// <summary>
/// Draws a laid-out graph as a PNG, mirroring <see cref="SvgRenderer"/>. Word
/// embeds raster images, so this is what ends up in the .docx.
/// </summary>
public static class PngRenderer
{
    /// <summary>Supersampling factor: draw large, scale down, get smooth edges.</summary>
    private const float Scale = 2f;

    /// <summary>
    /// Ceiling on the raster, in pixels. A GDI+ bitmap is a single allocation of
    /// width * height * 4 bytes, and it throws a bare "Parameter is not valid"
    /// long before it runs out of memory. A 100 table entity relationship
    /// diagram supersampled 2x is comfortably past that, which used to take the
    /// whole run down. 80 megapixels is about 320 MB and renders fine.
    /// </summary>
    private const long MaxPixels = 80_000_000;

    /// <summary>Neither dimension may exceed this; GDI+ struggles well before int overflow.</summary>
    private const int MaxDimension = 20_000;

    public static void Render(Graph graph, LayoutResult size, string outputPath, DiagramTheme? theme = null)
    {
        theme ??= DiagramTheme.Brand;

        // Shrink rather than fail. The SVG alongside this is always exact, so a
        // scaled-down raster loses fidelity, not information.
        var scale = (double)Scale;
        var unscaledWidth = Math.Max(1.0, size.Width);
        var unscaledHeight = Math.Max(1.0, size.Height);

        var byDimension = Math.Min(
            MaxDimension / unscaledWidth,
            MaxDimension / unscaledHeight);
        var byArea = Math.Sqrt(MaxPixels / (unscaledWidth * unscaledHeight));
        scale = Math.Min(scale, Math.Min(byDimension, byArea));
        // Below this the text is unreadable anyway, and the caller is better off
        // with the SVG; but still produce something rather than nothing.
        scale = Math.Max(scale, 0.05);

        var width = Math.Max(1, (int)Math.Ceiling(unscaledWidth * scale));
        var height = Math.Max(1, (int)Math.Ceiling(unscaledHeight * scale));

        using var bitmap = new Bitmap(width, height);
        bitmap.SetResolution((float)(96 * scale), (float)(96 * scale));

        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.ScaleTransform((float)scale, (float)scale);
            graphics.Clear(Parse(theme.Canvas));

            // Edges, then nodes, then edge labels, so labels sit on top.
            foreach (var edge in graph.Edges) DrawEdge(graphics, edge, theme);
            foreach (var node in graph.Nodes.Where(n => !n.IsDummy)) DrawNode(graphics, node, theme);
            foreach (var edge in graph.Edges) DrawEdgeLabel(graphics, edge, theme);
        }

        bitmap.Save(outputPath, ImageFormat.Png);
    }

    private static void DrawEdge(Graphics g, Edge edge, DiagramTheme theme)
    {
        if (edge.Waypoints.Count < 2) return;

        using var pen = new Pen(Parse(theme.EdgeColour), 1.4f);
        if (edge.Dashed) pen.DashPattern = new[] { 5f, 3f };
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;

        var points = edge.Waypoints.Select(p => new PointF((float)p.X, (float)p.Y)).ToArray();
        if (points.Length == 2)
        {
            g.DrawLine(pen, points[0], points[1]);
        }
        else
        {
            using var path = new GraphicsPath();
            path.AddCurve(points, 0.35f);
            g.DrawPath(pen, path);
        }

        if (edge.ToEnding == EdgeEnding.Arrow)
            DrawArrowHead(g, points[^2], points[^1], theme);
        else if (edge.ToEnding == EdgeEnding.CrowsFoot)
            DrawCrowsFoot(g, points[^2], points[^1], theme);

        if (edge.FromEnding == EdgeEnding.Arrow)
            DrawArrowHead(g, points[1], points[0], theme);
        else if (edge.FromEnding == EdgeEnding.CrowsFoot)
            DrawCrowsFoot(g, points[1], points[0], theme);

    }

    private static void DrawEdgeLabel(Graphics g, Edge edge, DiagramTheme theme)
    {
        if (string.IsNullOrEmpty(edge.Label) || edge.Waypoints.Count < 2) return;

        var mid = SvgRenderer.Midpoint(edge);
        using var font = Font(theme, 8f, FontStyle.Regular);
        var textSize = g.MeasureString(edge.Label, font);
        var box = new RectangleF(
            (float)mid.X - textSize.Width / 2 - 3,
            (float)mid.Y - textSize.Height / 2,
            textSize.Width + 6,
            textSize.Height);
        using var backdrop = new SolidBrush(Color.FromArgb(240, Parse(theme.Surface)));
        g.FillRectangle(backdrop, box);
        using var brush = new SolidBrush(Parse(theme.Muted));
        g.DrawString(edge.Label, font, brush, box.X + 3, box.Y);
    }

    private static void DrawArrowHead(Graphics g, PointF from, PointF to, DiagramTheme theme)
    {
        var angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
        const double size = 8;
        const double spread = Math.PI / 7;
        var p1 = new PointF(
            (float)(to.X - size * Math.Cos(angle - spread)),
            (float)(to.Y - size * Math.Sin(angle - spread)));
        var p2 = new PointF(
            (float)(to.X - size * Math.Cos(angle + spread)),
            (float)(to.Y - size * Math.Sin(angle + spread)));
        using var brush = new SolidBrush(Parse(theme.EdgeColour));
        g.FillPolygon(brush, new[] { to, p1, p2 });
    }

    private static void DrawCrowsFoot(Graphics g, PointF from, PointF to, DiagramTheme theme)
    {
        var angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
        const double size = 9;
        const double spread = Math.PI / 6;
        using var pen = new Pen(Parse(theme.EdgeColour), 1.4f);
        var back = new PointF(
            (float)(to.X - size * Math.Cos(angle)),
            (float)(to.Y - size * Math.Sin(angle)));
        g.DrawLine(pen, to, new PointF(
            (float)(to.X - size * Math.Cos(angle - spread)),
            (float)(to.Y - size * Math.Sin(angle - spread))));
        g.DrawLine(pen, to, new PointF(
            (float)(to.X - size * Math.Cos(angle + spread)),
            (float)(to.Y - size * Math.Sin(angle + spread))));
        g.DrawLine(pen, to, back);
    }

    private static void DrawNode(Graphics g, Node node, DiagramTheme theme)
    {
        var accent = Parse(node.AccentColour ?? theme.Primary);
        var bounds = new RectangleF((float)node.Left, (float)node.Top,
            (float)node.Width, (float)node.Height);

        switch (node.Shape)
        {
            case NodeShape.Diamond:
                DrawDiamond(g, node, theme, accent);
                return;
            case NodeShape.Stadium:
                DrawSimpleBox(g, node, bounds, theme, accent, (float)node.Height / 2);
                return;
            case NodeShape.RoundedBox:
                DrawSimpleBox(g, node, bounds, theme, accent, (float)theme.CornerRadius);
                return;
        }

        var radius = (float)theme.CornerRadius;
        using (var body = RoundedRect(bounds, radius))
        using (var fill = new SolidBrush(Parse(theme.Surface)))
        using (var border = new Pen(Parse(theme.Border), 1f))
        {
            g.FillPath(fill, body);
            g.DrawPath(border, body);
        }

        var headerHeight = (float)NodeMeasurement.HeaderHeight(node);
        var headerRect = new RectangleF(bounds.X, bounds.Y, bounds.Width, headerHeight);
        using (var header = RoundedRectTopOnly(headerRect, radius))
        using (var headerBrush = new SolidBrush(accent))
        {
            g.FillPath(headerBrush, header);
        }

        var textX = bounds.X + (float)NodeMeasurement.PaddingX;
        var y = bounds.Y + (float)NodeMeasurement.PaddingY - 1;
        using (var titleFont = Font(theme, (float)NodeMeasurement.TitleFontSize, FontStyle.Bold))
        using (var white = new SolidBrush(Color.White))
        {
            g.DrawString(node.Title, titleFont, white, textX, y);
            y += titleFont.Height - 2;
            if (!string.IsNullOrEmpty(node.Subtitle))
            {
                using var subtitleFont = Font(theme, (float)NodeMeasurement.SubtitleFontSize, FontStyle.Italic);
                using var faded = new SolidBrush(Color.FromArgb(210, Color.White));
                g.DrawString(node.Subtitle, subtitleFont, faded, textX, y);
            }
        }

        y = bounds.Y + headerHeight + (float)NodeMeasurement.PaddingY - 2;
        var hasMarkers = node.Lines.Any(l => !string.IsNullOrEmpty(l.Marker));
        using var lineFont = Font(theme, (float)NodeMeasurement.LineFontSize, FontStyle.Regular);
        using var boldLineFont = Font(theme, (float)NodeMeasurement.LineFontSize, FontStyle.Bold);
        using var markerFont = Font(theme, (float)NodeMeasurement.LineFontSize - 1.5f, FontStyle.Bold);
        using var textBrush = new SolidBrush(Parse(theme.Text));
        using var markerBrush = new SolidBrush(Parse(theme.PrimaryDark));

        foreach (var line in node.Lines)
        {
            if (!string.IsNullOrEmpty(line.Marker))
                g.DrawString(line.Marker, markerFont, markerBrush, textX, y + 1);
            g.DrawString(line.Text, line.Emphasise ? boldLineFont : lineFont, textBrush,
                textX + (hasMarkers ? (float)NodeMeasurement.MarkerColumnWidth : 0f), y);
            y += (float)(NodeMeasurement.LineFontSize + NodeMeasurement.LineSpacing);
        }
    }

    private static void DrawSimpleBox(Graphics g, Node node, RectangleF bounds,
        DiagramTheme theme, Color accent, float radius)
    {
        using (var path = RoundedRect(bounds, Math.Min(radius, bounds.Height / 2)))
        using (var fill = new SolidBrush(Parse(theme.TintLight)))
        using (var border = new Pen(accent, 1.5f))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }
        DrawCentredLabel(g, node, theme);
    }

    private static void DrawDiamond(Graphics g, Node node, DiagramTheme theme, Color accent)
    {
        var points = new[]
        {
            new PointF((float)node.X, (float)node.Top),
            new PointF((float)node.Right, (float)node.Y),
            new PointF((float)node.X, (float)node.Bottom),
            new PointF((float)node.Left, (float)node.Y),
        };
        using (var fill = new SolidBrush(Parse(theme.TintMid)))
        using (var border = new Pen(accent, 1.5f))
        {
            g.FillPolygon(fill, points);
            g.DrawPolygon(border, points);
        }
        DrawCentredLabel(g, node, theme);
    }

    private static void DrawCentredLabel(Graphics g, Node node, DiagramTheme theme)
    {
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        using var font = Font(theme, (float)NodeMeasurement.TitleFontSize - 1, FontStyle.Bold);
        using var brush = new SolidBrush(Parse(theme.Text));
        var hasSubtitle = !string.IsNullOrEmpty(node.Subtitle);
        var box = new RectangleF((float)node.Left, (float)node.Top,
            (float)node.Width, (float)node.Height);
        if (hasSubtitle) box.Height -= 12;
        g.DrawString(node.Title, font, brush, box, format);

        if (hasSubtitle)
        {
            using var subtitleFont = Font(theme, (float)NodeMeasurement.SubtitleFontSize, FontStyle.Regular);
            using var muted = new SolidBrush(Parse(theme.Muted));
            var subtitleBox = new RectangleF((float)node.Left, (float)node.Bottom - 18,
                (float)node.Width, 16);
            g.DrawString(node.Subtitle, subtitleFont, muted, subtitleBox, format);
        }
    }

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(0.1f, radius * 2);
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath RoundedRectTopOnly(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(0.1f, radius * 2);
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddLine(bounds.Right, bounds.Bottom, bounds.X, bounds.Bottom);
        path.CloseFigure();
        return path;
    }

    private static Font Font(DiagramTheme theme, float size, FontStyle style)
    {
        try
        {
            return new Font(theme.RenderFontFamily, size, style, GraphicsUnit.Pixel);
        }
        catch
        {
            return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Pixel);
        }
    }

    private static Color Parse(string hex)
    {
        var value = hex.TrimStart('#');
        if (value.Length == 6
            && int.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        }
        return Color.Black;
    }
}

