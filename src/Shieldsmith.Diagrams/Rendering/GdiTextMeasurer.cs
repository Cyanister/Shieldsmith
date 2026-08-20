using System.Drawing;
using System.Drawing.Text;
using Shieldsmith.Diagrams.Layout;

namespace Shieldsmith.Diagrams.Rendering;

/// <summary>
/// Measures text with GDI+ so layout matches what the PNG renderer draws.
/// Falls back to a deterministic approximation if a font cannot be created,
/// which keeps layout working on a machine missing the preferred family.
/// </summary>
public sealed class GdiTextMeasurer : ITextMeasurer, IDisposable
{
    private readonly Bitmap _surface;
    private readonly Graphics _graphics;
    private readonly string _family;
    private readonly Dictionary<(string, double, bool), SizeD> _cache = new();

    public GdiTextMeasurer(string fontFamily = "Segoe UI")
    {
        _family = fontFamily;
        _surface = new Bitmap(1, 1);
        _graphics = Graphics.FromImage(_surface);
        _graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
    }

    public SizeD Measure(string text, double fontSize, bool bold)
    {
        if (string.IsNullOrEmpty(text)) return new SizeD(0, fontSize * 1.35);
        var key = (text, fontSize, bold);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        SizeD size;
        try
        {
            using var font = new Font(_family, (float)fontSize,
                bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            var measured = _graphics.MeasureString(text, font, int.MaxValue,
                StringFormat.GenericTypographic);
            // GenericTypographic trims trailing space; add a little back so text
            // never touches the box edge.
            size = new SizeD(measured.Width + 2, measured.Height + 2);
        }
        catch
        {
            size = new SizeD(text.Length * fontSize * 0.55, fontSize * 1.35);
        }

        _cache[key] = size;
        return size;
    }

    public void Dispose()
    {
        _graphics.Dispose();
        _surface.Dispose();
    }
}
