using System.Diagnostics;
using System.Text;

namespace Shieldsmith.Outputs.Diagrams;

/// <summary>
/// Finds and actually invokes Graphviz. Absence of Graphviz is a supported state,
/// reported through IsAvailable, never an exception at render time.
/// </summary>
public sealed class GraphvizRunner
{
    private readonly string? _dotPath;

    public GraphvizRunner() : this(FindDotExecutable()) { }

    public GraphvizRunner(string? dotPath) => _dotPath = dotPath;

    public bool IsAvailable => _dotPath is not null;
    public string? DotPath => _dotPath;

    public static string? FindDotExecutable()
    {
        // 1. Explicit override.
        var overridePath = Environment.GetEnvironmentVariable("GRAPHVIZ_DOT");
        if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
            return overridePath;

        // 2. PATH.
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir.Trim(), "dot.exe");
            if (File.Exists(candidate)) return candidate;
        }

        // 3. Default install locations.
        foreach (var programFiles in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            if (string.IsNullOrEmpty(programFiles) || !Directory.Exists(programFiles)) continue;
            foreach (var graphvizDir in Directory.EnumerateDirectories(programFiles, "Graphviz*"))
            {
                var candidate = Path.Combine(graphvizDir, "bin", "dot.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    /// <summary>Renders a DOT file to the requested format. Throws GraphvizException on failure.</summary>
    public void Render(string dotFilePath, string outputPath, string format, TimeSpan? timeout = null)
    {
        var output = Run($"-T{format} \"{dotFilePath}\" -o \"{outputPath}\"", timeout);
        if (!File.Exists(outputPath))
            throw new GraphvizException($"Graphviz reported success but produced no {format} file. Output: {output}");
    }

    /// <summary>Runs dot -Tplain and returns its stdout for layout parsing.</summary>
    public string RenderPlain(string dotFilePath, TimeSpan? timeout = null) =>
        Run($"-Tplain \"{dotFilePath}\"", timeout);

    private string Run(string arguments, TimeSpan? timeout)
    {
        if (_dotPath is null)
            throw new GraphvizException("Graphviz is not installed or could not be found.");

        var info = new ProcessStartInfo(_dotPath, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = Process.Start(info)
            ?? throw new GraphvizException("Graphviz process failed to start.");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)(timeout ?? TimeSpan.FromSeconds(60)).TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw new GraphvizException("Graphviz timed out laying out the diagram.");
        }

        if (process.ExitCode != 0)
            throw new GraphvizException($"Graphviz exited with code {process.ExitCode}: {stderr.Result}");

        return stdout.Result;
    }
}

public sealed class GraphvizException : Exception
{
    public GraphvizException(string message) : base(message) { }
}
