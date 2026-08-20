using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using Shieldsmith.Ai.Providers;
using Shieldsmith.Core.Models;
using Shieldsmith.Outputs.Diagrams;

namespace Shieldsmith.App;

public partial class AboutWindow : Window
{
    public AboutWindow(SolutionModel? loaded)
    {
        InitializeComponent();

        var assembly = Assembly.GetExecutingAssembly();
        txtVersion.Text = Version(assembly);
        txtBuilt.Text = BuildDate(assembly);
        txtRuntime.Text = $".NET {Environment.Version} on {Environment.OSVersion.VersionString}";
        txtEngines.Text = Engines();
        txtLoaded.Text = loaded is null
            ? "None. Drop a solution export on the main window to analyse one."
            : $"{loaded.DisplayName} ({loaded.UniqueName}) version {loaded.Version}, " +
              $"{(loaded.IsManaged ? "managed" : "unmanaged")}, " +
              $"published by {loaded.PublisherDisplayName}.";
        txtCopyright.Text = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
                            ?? "Copyright Cameron Shields";
    }

    /// <summary>
    /// The informational version, which carries the real "0.9.0" rather than
    /// the four-part assembly version. A source-control suffix after '+' is
    /// trimmed: it is noise to a user reading an About box.
    /// </summary>
    private static string Version(Assembly assembly)
    {
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
            return assembly.GetName().Version?.ToString() ?? "unknown";
        var plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }

    /// <summary>
    /// Stamped at compile time by Directory.Build.props. A file timestamp would
    /// be wrong, because copying the executable rewrites it.
    /// </summary>
    private static string BuildDate(Assembly assembly)
    {
        var stamped = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildDate")?.Value;
        if (string.IsNullOrWhiteSpace(stamped)) return "not recorded in this build";
        return DateTime.TryParse(stamped, out var date)
            ? date.ToString("d MMMM yyyy")
            : stamped;
    }

    /// <summary>What this machine can actually do, in the same words as the header pills.</summary>
    private static string Engines()
    {
        var parts = new List<string>
        {
            Shieldsmith.Mermaid.MermaidRenderer.RuntimeVersion is { } webView
                ? $"Mermaid via WebView2 {webView}"
                : "Mermaid unavailable (no WebView2 runtime)",
            "built-in layout engine (always available)",
            GraphvizRunner.FindDotExecutable() is null ? "Graphviz not installed" : "Graphviz installed",
            ClaudeCodeCliProvider.FindCli() is null ? "Claude Code CLI not found" : "Claude Code CLI found",
        };
        return string.Join("\n", parts);
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true,
        });
        e.Handled = true;
    }
}
