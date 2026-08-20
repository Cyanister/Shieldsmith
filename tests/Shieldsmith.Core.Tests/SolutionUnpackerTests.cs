using System.IO.Compression;
using Shieldsmith.Core.Parsing;
using Xunit;

namespace Shieldsmith.Core.Tests;

public sealed class SolutionUnpackerTests
{
    [Fact]
    public void Rejects_zip_entries_that_escape_the_extraction_directory()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"shieldsmith-zipslip-{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../evil.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("escaped");
            }

            Assert.Throws<SolutionFormatException>(() => SolutionUnpacker.Unpack(zipPath));
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Fact]
    public void Rejects_a_zip_that_is_not_a_solution()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"shieldsmith-notsolution-{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("readme.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("not a solution");
            }

            Assert.Throws<SolutionFormatException>(() => SolutionUnpacker.Unpack(zipPath));
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Fact]
    public void Dispose_removes_the_temp_directory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "data", "ContosoTravel_sample.zip")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var zipPath = Path.Combine(dir!.FullName, "data", "ContosoTravel_sample.zip");

        string rootPath;
        using (var unpacked = SolutionUnpacker.Unpack(zipPath))
        {
            rootPath = unpacked.RootPath;
            Assert.True(Directory.Exists(rootPath));
        }
        Assert.False(Directory.Exists(rootPath));
    }

    [Fact]
    public void Source_zip_can_be_opened_while_locked_for_write_share()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "data", "ContosoTravel_sample.zip")))
            dir = dir.Parent;
        var zipPath = Path.Combine(dir!.FullName, "data", "ContosoTravel_sample.zip");

        // Simulate another process holding the file open (read share only, as
        // Explorer preview or an email client would).
        using var holder = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var unpacked = SolutionUnpacker.Unpack(zipPath);
        Assert.True(File.Exists(unpacked.SolutionXmlPath));
    }
}
