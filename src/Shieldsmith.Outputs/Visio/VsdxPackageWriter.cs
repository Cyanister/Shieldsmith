using System.IO.Packaging;
using System.Text;
using System.Xml.Linq;
using Shieldsmith.Core.Models;
using Shieldsmith.Outputs.Diagrams;

namespace Shieldsmith.Outputs.Visio;

/// <summary>
/// Writes a real .vsdx file: the OPC package Visio actually opens, built part by
/// part with System.IO.Packaging. Entities are rectangles with their key columns
/// as text; relationships are straight one-dimensional line shapes with labels,
/// dashed when many-to-many or inferred. Positions come from the ERD layout so
/// the Visio drawing matches the rendered diagram.
/// </summary>
public static class VsdxPackageWriter
{
    private static readonly XNamespace V = "http://schemas.microsoft.com/office/visio/2012/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private const string DocumentRelType = "http://schemas.microsoft.com/visio/2010/relationships/document";
    private const string PagesRelType = "http://schemas.microsoft.com/visio/2010/relationships/pages";
    private const string PageRelType = "http://schemas.microsoft.com/visio/2010/relationships/page";

    private const string DocumentContentType = "application/vnd.ms-visio.drawing.main+xml";
    private const string PagesContentType = "application/vnd.ms-visio.pages+xml";
    private const string PageContentType = "application/vnd.ms-visio.page+xml";

    private const double Margin = 0.5;

    public static void Write(SolutionModel model, ErdLayout layout, string outputPath)
    {
        if (!outputPath.EndsWith(".vsdx", StringComparison.OrdinalIgnoreCase))
            outputPath = Path.ChangeExtension(outputPath, ".vsdx");

        var pageWidth = Math.Max(8.5, layout.Width + 2 * Margin);
        var pageHeight = Math.Max(11, layout.Height + 2 * Margin);

        using var package = Package.Open(outputPath, FileMode.Create);

        // visio/document.xml
        var documentUri = new Uri("/visio/document.xml", UriKind.Relative);
        var documentPart = package.CreatePart(documentUri, DocumentContentType, CompressionOption.Normal);
        WriteXml(documentPart, new XDocument(
            new XElement(V + "VisioDocument",
                new XAttribute(XNamespace.Xml + "space", "preserve"),
                new XElement(V + "DocumentSettings",
                    new XAttribute("TopPage", "0"),
                    new XAttribute("DefaultTextStyle", "0"),
                    new XAttribute("DefaultLineStyle", "0"),
                    new XAttribute("DefaultFillStyle", "0")))));
        package.CreateRelationship(documentUri, TargetMode.Internal, DocumentRelType, "rId1");

        // visio/pages/pages.xml
        var pagesUri = new Uri("/visio/pages/pages.xml", UriKind.Relative);
        var pagesPart = package.CreatePart(pagesUri, PagesContentType, CompressionOption.Normal);
        WriteXml(pagesPart, new XDocument(
            new XElement(V + "Pages",
                new XAttribute(XNamespace.Xml + "space", "preserve"),
                new XElement(V + "Page",
                    new XAttribute("ID", "0"),
                    new XAttribute("NameU", "Page-1"),
                    new XAttribute("Name", "Page-1"),
                    new XElement(V + "PageSheet",
                        new XAttribute("LineStyle", "0"),
                        new XAttribute("FillStyle", "0"),
                        new XAttribute("TextStyle", "0"),
                        Cell("PageWidth", Number(pageWidth)),
                        Cell("PageHeight", Number(pageHeight)),
                        Cell("PageScale", "1"),
                        Cell("DrawingScale", "1")),
                    new XElement(V + "Rel",
                        new XAttribute(R + "id", "rId1"))))));
        documentPart.CreateRelationship(new Uri("pages/pages.xml", UriKind.Relative),
            TargetMode.Internal, PagesRelType, "rId1");

        // visio/pages/page1.xml
        var pageUri = new Uri("/visio/pages/page1.xml", UriKind.Relative);
        var pagePart = package.CreatePart(pageUri, PageContentType, CompressionOption.Normal);
        WriteXml(pagePart, BuildPageContents(model, layout, pageHeight));
        pagesPart.CreateRelationship(new Uri("page1.xml", UriKind.Relative),
            TargetMode.Internal, PageRelType, "rId1");
    }

    private static XDocument BuildPageContents(SolutionModel model, ErdLayout layout, double pageHeight)
    {
        var shapes = new XElement(V + "Shapes");
        var shapeId = 1;
        var nodeIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // The layout origin is bottom-left, same as Visio: only the margin shifts.
        foreach (var node in layout.Nodes)
        {
            var entity = model.FindEntity(node.Id);
            shapes.Add(EntityShape(shapeId, node, entity));
            nodeIds[node.Id] = shapeId;
            shapeId++;
        }

        foreach (var relationship in model.OneToManyRelationships)
            AddConnector(shapes, layout, nodeIds, ref shapeId,
                relationship.ReferencingEntity, relationship.ReferencedEntity,
                $"{relationship.ReferencingAttribute}  (*..1)", dashed: false);

        foreach (var relationship in model.ManyToManyRelationships)
            AddConnector(shapes, layout, nodeIds, ref shapeId,
                relationship.Entity1, relationship.Entity2,
                $"{relationship.IntersectEntity}  (*..*)", dashed: true);

        return new XDocument(
            new XElement(V + "PageContents",
                new XAttribute(XNamespace.Xml + "space", "preserve"),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                shapes));
    }

    private static XElement EntityShape(int id, ErdNode node, EntityModel? entity)
    {
        var text = new StringBuilder();
        var title = entity is null || string.IsNullOrEmpty(entity.DisplayName)
            ? node.Id
            : entity.DisplayName;
        text.AppendLine(title);
        if (entity is not null)
        {
            text.AppendLine($"({entity.LogicalName})");
            foreach (var attribute in entity.Attributes)
                text.AppendLine(DotBuilder.AttributeLine(attribute));
        }

        return new XElement(V + "Shape",
            new XAttribute("ID", id),
            new XAttribute("NameU", $"Entity.{id}"),
            new XAttribute("Name", title),
            new XAttribute("Type", "Shape"),
            new XAttribute("LineStyle", "0"),
            new XAttribute("FillStyle", "0"),
            new XAttribute("TextStyle", "0"),
            Cell("PinX", Number(node.X + Margin)),
            Cell("PinY", Number(node.Y + Margin)),
            Cell("Width", Number(node.Width)),
            Cell("Height", Number(node.Height)),
            Cell("LocPinX", Number(node.Width / 2), "Width*0.5"),
            Cell("LocPinY", Number(node.Height / 2), "Height*0.5"),
            Cell("FillForegnd", "#E8F1FB"),
            Cell("LineColor", "#2B579A"),
            Cell("LineWeight", "0.02"),
            Cell("VerticalAlign", "0"),
            Cell("Para.HorzAlign", "0"),
            Cell("Char.Size", "0.11111"),
            RectangleGeometry(),
            new XElement(V + "Text", text.ToString()));
    }

    private static void AddConnector(XElement shapes, ErdLayout layout,
        IReadOnlyDictionary<string, int> nodeIds, ref int shapeId,
        string fromEntity, string toEntity, string label, bool dashed)
    {
        var from = layout.FindNode(fromEntity);
        var to = layout.FindNode(toEntity);
        if (from is null || to is null) return;
        if (!nodeIds.ContainsKey(from.Id) || !nodeIds.ContainsKey(to.Id)) return;

        var x1 = from.X + Margin;
        var y1 = from.Y + Margin;
        var x2 = to.X + Margin;
        var y2 = to.Y + Margin;
        var width = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        if (width < 0.01) return;

        var shape = new XElement(V + "Shape",
            new XAttribute("ID", shapeId),
            new XAttribute("NameU", $"Relationship.{shapeId}"),
            new XAttribute("Name", label),
            new XAttribute("Type", "Shape"),
            new XAttribute("LineStyle", "0"),
            new XAttribute("FillStyle", "0"),
            new XAttribute("TextStyle", "0"),
            Cell("BeginX", Number(x1)),
            Cell("BeginY", Number(y1)),
            Cell("EndX", Number(x2)),
            Cell("EndY", Number(y2)),
            Cell("PinX", Number((x1 + x2) / 2)),
            Cell("PinY", Number((y1 + y2) / 2)),
            Cell("Width", Number(width)),
            Cell("Height", "0"),
            Cell("LocPinX", Number(width / 2), "Width*0.5"),
            Cell("LocPinY", "0", "Height*0.5"),
            Cell("Angle", Number(Math.Atan2(y2 - y1, x2 - x1))),
            Cell("ObjType", "2"),
            Cell("LineColor", "#5A5A5A"),
            Cell("LineWeight", "0.014"),
            Cell("Char.Size", "0.09722"),
            dashed ? Cell("LinePattern", "2") : Cell("LinePattern", "1"),
            LineGeometry(width),
            new XElement(V + "Text", label));

        shapes.Add(shape);
        shapeId++;
    }

    private static XElement RectangleGeometry() =>
        new(V + "Section",
            new XAttribute("N", "Geometry"),
            new XAttribute("IX", "0"),
            Cell("NoFill", "0"),
            Cell("NoLine", "0"),
            GeometryRow("RelMoveTo", 1, "0", "0"),
            GeometryRow("RelLineTo", 2, "1", "0"),
            GeometryRow("RelLineTo", 3, "1", "1"),
            GeometryRow("RelLineTo", 4, "0", "1"),
            GeometryRow("RelLineTo", 5, "0", "0"));

    private static XElement LineGeometry(double width) =>
        new(V + "Section",
            new XAttribute("N", "Geometry"),
            new XAttribute("IX", "0"),
            Cell("NoFill", "1"),
            Cell("NoLine", "0"),
            new XElement(V + "Row",
                new XAttribute("T", "MoveTo"),
                new XAttribute("IX", "1"),
                Cell("X", "0"),
                Cell("Y", "0")),
            new XElement(V + "Row",
                new XAttribute("T", "LineTo"),
                new XAttribute("IX", "2"),
                Cell("X", Number(width), "Width"),
                Cell("Y", "0")));

    private static XElement GeometryRow(string type, int index, string x, string y) =>
        new(V + "Row",
            new XAttribute("T", type),
            new XAttribute("IX", index),
            Cell("X", x),
            Cell("Y", y));

    private static XElement Cell(string name, string value, string? formula = null)
    {
        var cell = new XElement(V + "Cell",
            new XAttribute("N", name),
            new XAttribute("V", value));
        if (formula is not null)
            cell.Add(new XAttribute("F", formula));
        return cell;
    }

    private static string Number(double value) =>
        value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    private static void WriteXml(PackagePart part, XDocument document)
    {
        using var stream = part.GetStream(FileMode.Create, FileAccess.Write);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        document.Declaration = new XDeclaration("1.0", "utf-8", "yes");
        document.Save(writer);
    }
}
