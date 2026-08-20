using System.IO;
using System.Windows;
using Shieldsmith.Core.Parsing;

namespace Shieldsmith.App;

public partial class App : Application
{
    /// <summary>
    /// A solution zip passed on the command line, so Shieldsmith can be the
    /// "open with" handler for an export and can be driven from a script.
    /// </summary>
    public static string? StartupSolutionPath { get; private set; }

    /// <summary>
    /// Set by --shot &lt;path&gt;: capture the window to a PNG once the analysis
    /// has finished, then exit. Used to verify the interface renders.
    /// </summary>
    public static string? ScreenshotPath { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Housekeeping: remove extraction directories left by crashed sessions.
        SolutionUnpacker.SweepOrphans();

        for (var i = 0; i < e.Args.Length; i++)
        {
            var arg = e.Args[i];
            if (string.Equals(arg, "--shot", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
                ScreenshotPath = e.Args[++i];
            else if (arg.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(arg))
                StartupSolutionPath = Path.GetFullPath(arg);
        }
    }
}
