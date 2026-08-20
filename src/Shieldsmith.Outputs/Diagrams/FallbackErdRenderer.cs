using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Shieldsmith.Outputs.Diagrams;

/// <summary>
/// Draws an ErdLayout to a PNG with System.Drawing when Graphviz is not
/// available. Layout coordinates are inches, origin bottom-left; the bitmap is
/// pixels, origin top-left, so Y flips here.
/// </summary>
public static class FallbackErdRenderer
{
    private const int Dpi = 120;

    public static void RenderPng(ErdLayout layout, string outputPath)
    {
        if (layout.Nodes.Count == 0)
            throw new InvalidOperationException("The layout has no entities to draw.");

        var width = Math.Max(400, (int)Math.Ceiling(layout.Width * Dpi));
        var height = Math.Max(300, (int)Math.Ceiling(layout.Height * Dpi));

        using var bitmap = new Bitmap(width, height);
        bitmap.SetResolution(Dpi, Dpi);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(Color.White);

        using var boxFill = new SolidBrush(Color.FromArgb(232, 241, 251));
        using var titleFill = new SolidBrush(Color.FromArgb(43, 87, 154));
        using var boxBorder = new Pen(Color.FromArgb(43, 87, 154), 2f);
        using var edgePen = new Pen(Color.FromArgb(90, 90, 90), 1.6f);
        using var dashedPen = new Pen(Color.FromArgb(90, 90, 90), 1.6f) { DashStyle = DashStyle.Dash };
        using var titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var subtitleFont = new Font("Segoe UI", 7.5f, FontStyle.Italic);
        using var lineFont = new Font("Segoe UI", 8f);
        using var labelFont = new Font("Segoe UI", 7.5f);
        using var textBrush = new SolidBrush(Color.FromArgb(30, 30, 30));
        using var labelBrush = new SolidBrush(Color.FromArgb(90, 90, 90));

        // Edges first so boxes sit on top of the lines.
        foreach (var edge in layout.Edges)
        {
            var from = layout.FindNode(edge.FromId);
            var to = layout.FindNode(edge.ToId);
            if (from is null || to is null) continue;

            var p1 = Centre(from, layout);
            var p2 = Centre(to, layout);
            graphics.DrawLine(edge.Dashed ? dashedPen : edgePen, p1, p2);

            if (!string.IsNullOrEmpty(edge.Label))
            {
                // 40 percent along the line, clear of both boxes more often than a midpoint.
                var labelPoint = new PointF(
                    p1.X + (p2.X - p1.X) * 0.4f,
                    p1.Y + (p2.Y - p1.Y) * 0.4f - 14);
                var size = graphics.MeasureString(edge.Label, labelFont);
                graphics.FillRectangle(Brushes.White, labelPoint.X, labelPoint.Y, size.Width, size.Height);
                graphics.DrawString(edge.Label, labelFont, labelBrush, labelPoint);
            }
        }

        foreach (var node in layout.Nodes)
        {
            var rect = Rect(node, layout);
            graphics.FillRectangle(boxFill, rect);

            var titleBand = new RectangleF(rect.X, rect.Y, rect.Width, 34);
            graphics.FillRectangle(titleFill, titleBand);
            graphics.DrawRectangle(boxBorder, rect.X, rect.Y, rect.Width, rect.Height);

            graphics.DrawString(node.Title, titleFont, Brushes.White,
                new RectangleF(titleBand.X + 6, titleBand.Y + 2, titleBand.Width - 12, 20));
            graphics.DrawString(node.Subtitle, subtitleFont, Brushes.White,
                new RectangleF(titleBand.X + 6, titleBand.Y + 19, titleBand.Width - 12, 14));

            var y = rect.Y + titleBand.Height + 4;
            foreach (var line in node.Lines)
            {
                if (y + 14 > rect.Bottom) break;
                graphics.DrawString(line, lineFont, textBrush,
                    new RectangleF(rect.X + 6, y, rect.Width - 12, 14));
                y += 14;
            }
        }

        bitmap.Save(outputPath, ImageFormat.Png);
    }

    private static PointF Centre(ErdNode node, ErdLayout layout) =>
        new((float)(node.X * Dpi), (float)((layout.Height - node.Y) * Dpi));

    private static RectangleF Rect(ErdNode node, ErdLayout layout)
    {
        var centre = Centre(node, layout);
        var width = (float)(node.Width * Dpi);
        var height = (float)(node.Height * Dpi);
        return new RectangleF(centre.X - width / 2, centre.Y - height / 2, width, height);
    }
}
