using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Shieldsmith.Outputs.Word;

/// <summary>
/// The styles part for Shieldsmith reports: real named styles so the document has a
/// working navigation pane, a TOC that resolves, and consistent formatting a
/// reader can restyle in one place.
/// </summary>
internal static class WordStyles
{
    public const string Title = "ShieldsmithTitle";
    public const string Subtitle = "ShieldsmithSubtitle";
    public const string Heading1 = "Heading1";
    public const string Heading2 = "Heading2";
    public const string Heading3 = "Heading3";
    public const string TableStyle = "ShieldsmithTable";
    public const string Accent = "2B579A";

    public static void AddTo(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            DocDefaults(),
            NormalStyle(),
            TitleStyle(),
            SubtitleStyle(),
            HeadingStyle(Heading1, "heading 1", 1, "32"),
            HeadingStyle(Heading2, "heading 2", 2, "26"),
            HeadingStyle(Heading3, "heading 3", 3, "24"),
            TableGridStyle());
        stylesPart.Styles.Save();
    }

    private static DocDefaults DocDefaults() => new(
        new RunPropertiesDefault(new RunPropertiesBaseStyle(
            new RunFonts { Ascii = "Segoe UI", HighAnsi = "Segoe UI" },
            new FontSize { Val = "20" })),
        new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
            new SpacingBetweenLines { After = "120", Line = "264", LineRule = LineSpacingRuleValues.Auto })));

    private static Style NormalStyle() => new(
        new StyleName { Val = "Normal" },
        new PrimaryStyle())
    { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true };

    private static Style TitleStyle() => new(
        new StyleName { Val = "Shieldsmith Title" },
        new BasedOn { Val = "Normal" },
        new StyleParagraphProperties(
            new SpacingBetweenLines { Before = "2400", After = "240" }),
        new StyleRunProperties(
            new Bold(),
            new Color { Val = Accent },
            new FontSize { Val = "56" }))
    { Type = StyleValues.Paragraph, StyleId = Title };

    private static Style SubtitleStyle() => new(
        new StyleName { Val = "Shieldsmith Subtitle" },
        new BasedOn { Val = "Normal" },
        new StyleParagraphProperties(
            new SpacingBetweenLines { After = "120" }),
        new StyleRunProperties(
            new Color { Val = "595959" },
            new FontSize { Val = "28" }))
    { Type = StyleValues.Paragraph, StyleId = Subtitle };

    private static Style HeadingStyle(string styleId, string name, int outlineLevel, string fontSize) => new(
        new StyleName { Val = name },
        new BasedOn { Val = "Normal" },
        new NextParagraphStyle { Val = "Normal" },
        new PrimaryStyle(),
        new StyleParagraphProperties(
            new KeepNext(),
            new SpacingBetweenLines { Before = "320", After = "120" },
            new OutlineLevel { Val = outlineLevel - 1 }),
        new StyleRunProperties(
            new Bold(),
            new Color { Val = Accent },
            new FontSize { Val = fontSize }))
    { Type = StyleValues.Paragraph, StyleId = styleId };

    private static Style TableGridStyle() => new(
        new StyleName { Val = "Shieldsmith Table" },
        new BasedOn { Val = "TableNormal" },
        new StyleTableProperties(
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" })))
    { Type = StyleValues.Table, StyleId = TableStyle };
}
