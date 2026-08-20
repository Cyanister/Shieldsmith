namespace Shieldsmith.Core.Licensing;

/// <summary>
/// The single seam for a future edition split. Every premium-candidate feature
/// asks this gate; today the only implementation says yes to everything, which
/// keeps free, open-source and freemium all possible without carrying licensing
/// infrastructure that may never be needed.
/// </summary>
public interface IEditionGate
{
    bool IsEnabled(Feature feature);
}

public enum Feature
{
    WordReport,
    MarkdownReport,
    ErdDiagram,
    VisioExport,
    AiEnrichment,
    ExportPack,
    McpServer,
}

public sealed class AllFeaturesGate : IEditionGate
{
    public bool IsEnabled(Feature feature) => true;
}
