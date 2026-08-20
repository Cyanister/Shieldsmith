using System.IO.Packaging;
using Shieldsmith.Core.Models;
using Shieldsmith.Core.Parsing;
using Shieldsmith.Outputs.Diagrams;
using Shieldsmith.Outputs.Visio;
using Shieldsmith.Outputs.Word;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Xunit;

namespace Shieldsmith.Outputs.Tests;

public sealed class OutputsTests : IDisposable
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "data", "ContosoTravel_sample.zip")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }
    }

    private readonly UnpackedSolution _unpacked;
    private readonly SolutionModel _model;
    private readonly string _workDir;

    public OutputsTests()
    {
        _unpacked = SolutionUnpacker.Unpack(Path.Combine(RepoRoot, "data", "ContosoTravel_sample.zip"));
        _model = SolutionParser.Parse(_unpacked);
        _workDir = Path.Combine(Path.GetTempPath(), $"shieldsmith-outputs-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        _unpacked.Dispose();
        try { Directory.Delete(_workDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Dot_output_escapes_special_characters()
    {
        var model = new SolutionModel();
        var entity = new EntityModel { SchemaName = "x_test", LogicalName = "x_test", DisplayName = "A {weird|name} \"here\"" };
        entity.Attributes.Add(new AttributeModel
        {
            LogicalName = "x_col",
            DisplayName = "Pipes | and <angles>",
            Type = "nvarchar",
        });
        model.Entities.Add(entity);

        var dot = DotBuilder.Build(model);
        Assert.Contains("\\{weird\\|name\\}", dot);
        Assert.Contains("\\\"here\\\"", dot);
        Assert.Contains("Pipes \\| and \\<angles\\>", dot);
        Assert.DoesNotContain("🔑", dot);
    }

    [Fact]
    public void Fallback_layout_and_renderer_produce_a_png_without_graphviz()
    {
        var result = ErdGenerator.Generate(_model, _workDir, "erd",
            runner: new GraphvizRunner(dotPath: null));

        Assert.False(result.UsedGraphviz);
        Assert.NotNull(result.Warning);
        Assert.True(File.Exists(result.PngPath));
        Assert.True(new FileInfo(result.PngPath!).Length > 1000);
        Assert.Equal(4, result.Layout.Nodes.Count);
        Assert.Equal(3, result.Layout.Edges.Count);
    }

    [Fact]
    public void Plain_layout_reader_parses_the_dot_plain_format()
    {
        const string plain = "graph 1 8.5 11\n" +
                             "node \"ct_Trip\" 2.5 9 2.2 1.8 \"label text\" solid record black lightblue\n" +
                             "node ct_Booking 6 4.5 2 1.5 label solid record black lightblue\n" +
                             "edge ct_Booking \"ct_Trip\" 4 1 2 3 4 5 6 7 8 solid black\n" +
                             "stop\n";

        var layout = DotPlainLayoutReader.Parse(plain);
        Assert.Equal(8.5, layout.Width);
        Assert.Equal(11, layout.Height);
        Assert.Equal(2, layout.Nodes.Count);
        var trip = layout.FindNode("ct_Trip");
        Assert.NotNull(trip);
        Assert.Equal(2.5, trip!.X);
        Assert.Equal(1.8, trip.Height);
        var edge = Assert.Single(layout.Edges);
        Assert.Equal("ct_Booking", edge.FromId);
        Assert.Equal("ct_Trip", edge.ToId);
    }

    [Fact]
    public void Word_report_is_valid_openxml_and_embeds_the_diagram()
    {
        var erd = ErdGenerator.Generate(_model, _workDir, "erd", runner: new GraphvizRunner(dotPath: null));
        var docxPath = Path.Combine(_workDir, "report.docx");
        WordReportBuilder.Write(_model, docxPath, erd.PngPath);

        using var document = WordprocessingDocument.Open(docxPath, false);
        var validator = new OpenXmlValidator();
        var errors = validator.Validate(document).ToList();
        Assert.True(errors.Count == 0,
            string.Join("\n", errors.Select(e => $"{e.Description} ({e.Path?.XPath})")));

        // The ERD is genuinely embedded, not referenced by a note.
        Assert.Single(document.MainDocumentPart!.ImageParts);
        var text = document.MainDocumentPart.Document.Body!.InnerText;
        Assert.Contains("Contoso Travel", text);
        Assert.Contains("ct_trip_traveller", text);   // N:N intersect named
        Assert.Contains("System required", text);      // required levels resolved
        Assert.DoesNotContain("manually inserted", text);
    }

    [Fact]
    public void Ai_interpretation_renders_only_in_labelled_blocks()
    {
        var interpretation = new GeneratedInterpretation
        {
            Provider = "TestProvider",
            Model = "test-model",
            GeneratedAtUtc = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc),
            SolutionOverview = "This solution manages travel bookings.",
            RiskObservations = "No risks observed in the supplied data.",
        };
        interpretation.EntityDescriptions["ct_trip"] = "The Trip table is the hub of the model.";
        interpretation.FlowDescriptions["Send booking confirmation"] = "Emails on booking confirmation.";

        var docxPath = Path.Combine(_workDir, "ai-report.docx");
        WordReportBuilder.Write(_model, docxPath, erdPngPath: null, interpretation);
        using (var document = WordprocessingDocument.Open(docxPath, false))
        {
            var errors = new OpenXmlValidator().Validate(document).ToList();
            Assert.Empty(errors);
            var text = document.MainDocumentPart!.Document.Body!.InnerText;
            Assert.Contains("Generated interpretation (TestProvider, test-model", text);
            Assert.Contains("This solution manages travel bookings.", text);
            Assert.Contains("The Trip table is the hub of the model.", text);
            Assert.Contains("AI observations", text);
        }

        var markdownDir = Path.Combine(_workDir, "ai-markdown");
        Shieldsmith.Outputs.Markdown.MarkdownReportBuilder.Write(_model, markdownDir, null, interpretation);
        var index = File.ReadAllText(Path.Combine(markdownDir, "index.md"));
        Assert.Contains("> **Generated interpretation (TestProvider, test-model", index);
        var trip = File.ReadAllText(Path.Combine(markdownDir, "tables", "ct_trip.md"));
        Assert.Contains("> The Trip table is the hub of the model.", trip);
        // The fact tables themselves carry no AI text.
        Assert.DoesNotContain("hub of the model", trip.Split("## Columns")[1].Split("##")[0]);
    }

    [Fact]
    public void Markdown_output_produces_index_tables_and_flows()
    {
        var markdownDir = Path.Combine(_workDir, "markdown");
        Shieldsmith.Outputs.Markdown.MarkdownReportBuilder.Write(_model, markdownDir);

        Assert.True(File.Exists(Path.Combine(markdownDir, "index.md")));
        Assert.True(File.Exists(Path.Combine(markdownDir, "components.md")));
        Assert.True(File.Exists(Path.Combine(markdownDir, "relationships.md")));
        Assert.True(File.Exists(Path.Combine(markdownDir, "solution-artefacts.md")));
        Assert.True(File.Exists(Path.Combine(markdownDir, "tables", "ct_trip.md")));
        Assert.True(File.Exists(Path.Combine(markdownDir, "flows", "send-booking-confirmation.md")));

        var tripMarkdown = File.ReadAllText(Path.Combine(markdownDir, "tables", "ct_trip.md"));
        Assert.Contains("`ct_tripid`", tripMarkdown);
        Assert.Contains("System required", tripMarkdown);
        Assert.Contains("ct_trip_traveller", tripMarkdown);

        var flowMarkdown = File.ReadAllText(Path.Combine(markdownDir, "flows", "send-booking-confirmation.md"));
        Assert.Contains("ct_booking", flowMarkdown);
        Assert.Contains("SendEmailV2", flowMarkdown);
    }

    [Fact]
    public void Vsdx_is_a_valid_opc_package_with_the_visio_parts_and_shapes()
    {
        var layout = FallbackLayoutEngine.Layout(_model, showAttributes: true);
        var vsdxPath = Path.Combine(_workDir, "diagram.vsdx");
        VsdxPackageWriter.Write(_model, layout, vsdxPath);

        using var package = Package.Open(vsdxPath, FileMode.Open, FileAccess.Read);
        var parts = package.GetParts().Select(p => p.Uri.ToString()).ToList();
        Assert.Contains("/visio/document.xml", parts);
        Assert.Contains("/visio/pages/pages.xml", parts);
        Assert.Contains("/visio/pages/page1.xml", parts);

        // The package-level relationship points at the document part with the Visio type.
        var docRel = Assert.Single(package.GetRelationships());
        Assert.Equal("http://schemas.microsoft.com/visio/2010/relationships/document", docRel.RelationshipType);

        var pagePart = package.GetPart(new Uri("/visio/pages/page1.xml", UriKind.Relative));
        var pageXml = System.Xml.Linq.XDocument.Load(pagePart.GetStream());
        var ns = (System.Xml.Linq.XNamespace)"http://schemas.microsoft.com/office/visio/2012/main";
        var shapes = pageXml.Root!.Element(ns + "Shapes")!.Elements(ns + "Shape").ToList();

        // 4 entity boxes + 2 one-to-many + 1 many-to-many connector.
        Assert.Equal(7, shapes.Count);
        Assert.Contains(shapes, s => s.Element(ns + "Text")?.Value.Contains("Trip") == true);
        // The many-to-many connector is dashed (LinePattern 2).
        Assert.Contains(shapes, s =>
            s.Elements(ns + "Cell").Any(c => c.Attribute("N")?.Value == "LinePattern" && c.Attribute("V")?.Value == "2"));
    }
}
