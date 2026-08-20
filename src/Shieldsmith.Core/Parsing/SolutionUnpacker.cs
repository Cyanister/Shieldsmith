using System.IO.Compression;

namespace Shieldsmith.Core.Parsing;

/// <summary>
/// Extracts a solution export zip to a private temp directory. The zip is opened
/// share-read so a file still open in Outlook or Explorer preview does not fail,
/// entries are checked against path traversal, and the temp directory lives only
/// as long as the returned handle.
/// </summary>
public static class SolutionUnpacker
{
    private static string TempRoot => Path.Combine(Path.GetTempPath(), "Shieldsmith");

    public static UnpackedSolution Unpack(string zipFilePath)
    {
        if (!File.Exists(zipFilePath))
            throw new FileNotFoundException("Solution zip not found.", zipFilePath);

        var outputDirectory = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        var outputRoot = Path.GetFullPath(outputDirectory + Path.DirectorySeparatorChar);

        try
        {
            using var stream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue; // directory entry

                var destination = Path.GetFullPath(Path.Combine(outputDirectory, entry.FullName));
                if (!destination.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase))
                    throw new SolutionFormatException(
                        $"Zip entry '{entry.FullName}' escapes the extraction directory. Refusing to extract.");

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
        }
        catch
        {
            TryDelete(outputDirectory);
            throw;
        }

        var solutionXml = Path.Combine(outputDirectory, "solution.xml");
        var customizationsXml = Path.Combine(outputDirectory, "customizations.xml");
        if (!File.Exists(solutionXml) || !File.Exists(customizationsXml))
        {
            TryDelete(outputDirectory);
            throw new SolutionFormatException(
                "This zip is not a Power Platform solution export: solution.xml or customizations.xml is missing.");
        }

        return new UnpackedSolution(outputDirectory);
    }

    /// <summary>Deletes leftover extraction directories older than a day. Call at startup; never throws.</summary>
    public static void SweepOrphans()
    {
        try
        {
            if (!Directory.Exists(TempRoot)) return;
            var cutoff = DateTime.UtcNow.AddDays(-1);
            foreach (var dir in Directory.EnumerateDirectories(TempRoot))
            {
                try
                {
                    if (Directory.GetCreationTimeUtc(dir) < cutoff)
                        Directory.Delete(dir, recursive: true);
                }
                catch
                {
                    // A locked orphan is left for the next sweep.
                }
            }
        }
        catch
        {
            // Sweeping is best-effort housekeeping only.
        }
    }

    internal static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Left for SweepOrphans.
        }
    }
}

public sealed class UnpackedSolution : IDisposable
{
    public string RootPath { get; }

    internal UnpackedSolution(string rootPath) => RootPath = rootPath;

    public string SolutionXmlPath => Path.Combine(RootPath, "solution.xml");
    public string CustomizationsXmlPath => Path.Combine(RootPath, "customizations.xml");

    public void Dispose() => SolutionUnpacker.TryDelete(RootPath);
}

public sealed class SolutionFormatException : Exception
{
    public SolutionFormatException(string message) : base(message) { }
}
