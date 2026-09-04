using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Shieldsmith.Outputs.Word;

/// <summary>
/// The styles part for Shieldsmith reports: real named styles so the document has a
/// working navigation pane, a TOC that resolves, and consistent formatting a
/// reader can restyle in one place. Colours follow the Shieldsmith brand: teal
/// accents on slate text, matching the app and the diagrams.
/// </summary>
internal static class WordStyles
{
    public const string Title = "ShieldsmithTitle";
    public const string Subtitle = "ShieldsmithSubtitle";
    public const string Heading1 = "Heading1";
    public const string Heading2 = "Heading2";
    public const string Heading3 = "Heading3";
    public const string TableStyle = "ShieldsmithTable";

    /// <summary>Brand teal, dark enough to read as text on white.</summary>
    public const string Accent = "008B8B";
    /// <summary>Light teal tint for shaded blocks and banded rows.</summary>
    public const string AccentTint = "E8F7F7";
    /// <summary>Fainter tint for alternate table rows.</summary>
    public const string BandTint = "F4FBFB";
    /// <summary>Teal-tinged border grey, softer than a hard line.</summary>
    public const string BorderColour = "BFE3E3";
    /// <summary>Slate for secondary text, matching the app's muted colour.</summary>
    public const string MutedText = "475569";
    /// <summary>Body text slate, matching the app.</summary>
    public const string BodyText = "0F172A";

    public static void AddTo(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            DocDefaults(),
            NormalStyle(),
            TitleStyle(),
            SubtitleStyle(),
            HeadingStyle(Heading1, "heading 1", 1, "32", pageBreakBefore: true),
            HeadingStyle(Heading2, "heading 2", 2, "26"),
            HeadingStyle(Heading3, "heading 3", 3, "24"),
            TableGridStyle());
        stylesPart.Styles.Save();
    }

    private static DocDefaults DocDefaults() => new(
        new RunPropertiesDefault(new RunPropertiesBaseStyle(
            new RunFonts { Ascii = "Segoe UI", HighAnsi = "Segoe UI" },
            new Color { Val = BodyText },
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
            new SpacingBetweenLines { Before = "2400", After = "120" }),
        new StyleRunProperties(
            new Bold(),
            new Color { Val = Accent },
            new FontSize { Val = "56" }))
    { Type = StyleValues.Paragraph, StyleId = Title };

    private static Style SubtitleStyle() => new(
        new StyleName { Val = "Shieldsmith Subtitle" },
        new BasedOn { Val = "Normal" },
        new StyleParagraphProperties(
            // The teal rule under the subtitle is the cover page's one flourish.
            new ParagraphBorders(
                new BottomBorder { Val = BorderValues.Single, Size = 12, Color = Accent, Space = 8 }),
            new SpacingBetweenLines { After = "360" }),
        new StyleRunProperties(
            new Color { Val = MutedText },
            new FontSize { Val = "28" }))
    { Type = StyleValues.Paragraph, StyleId = Subtitle };

    private static Style HeadingStyle(string styleId, string name, int outlineLevel, string fontSize,
        bool pageBreakBefore = false)
    {
        var paragraphProperties = new StyleParagraphProperties(
            new KeepNext(),
            new KeepLines(),
            new SpacingBetweenLines { Before = "320", After = "120" },
            new OutlineLevel { Val = outlineLevel - 1 });
        // Each top-level section starts its own page: a reader navigating a
        // long document by section should never land mid-page. The schema fixes
        // the child order, so this slots in after KeepLines, not at the front.
        if (pageBreakBefore)
            paragraphProperties.InsertAfter(new PageBreakBefore(),
                paragraphProperties.GetFirstChild<KeepLines>());

        return new Style(
            new StyleName { Val = name },
            new BasedOn { Val = "Normal" },
            new NextParagraphStyle { Val = "Normal" },
            new PrimaryStyle(),
            paragraphProperties,
            new StyleRunProperties(
                new Bold(),
                new Color { Val = Accent },
                new FontSize { Val = fontSize }))
        { Type = StyleValues.Paragraph, StyleId = styleId };
    }

    private static Style TableGridStyle() => new(
        new StyleName { Val = "Shieldsmith Table" },
        new BasedOn { Val = "TableNormal" },
        new StyleTableProperties(
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = BorderColour },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = BorderColour },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = BorderColour },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = BorderColour },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = BorderColour },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = BorderColour }),
            // Breathing room in every cell; text glued to a border reads badly.
            new TableCellMarginDefault(
                new TopMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                new TableCellLeftMargin { Width = 108, Type = TableWidthValues.Dxa },
                new BottomMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                new TableCellRightMargin { Width = 108, Type = TableWidthValues.Dxa })))
    { Type = StyleValues.Table, StyleId = TableStyle };
}
