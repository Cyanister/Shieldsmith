using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Shieldsmith.Ai.Prompting;

/// <summary>
/// Caches AI results on disk keyed by SHA-256 of (prompt + provider + model +
/// prompt version), so regenerating a document reuses answers unless the
/// underlying component, provider or prompt changed.
/// </summary>
public sealed class AiCache
{
    private readonly string _rootDirectory;

    public AiCache(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Shieldsmith", "cache");
    }

    public sealed class Entry
    {
        public string Text { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public DateTime GeneratedAtUtc { get; set; }
    }

    public string KeyFor(AiRequest request, string providerName, string model)
    {
        var material = string.Join("",
            PromptBuilder.PromptVersion, providerName, model, request.SystemPrompt, request.UserPrompt);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    public Entry? Get(string solutionName, string key)
    {
        try
        {
            var path = PathFor(solutionName, key);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<Entry>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public void Put(string solutionName, string key, Entry entry)
    {
        try
        {
            var path = PathFor(solutionName, key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(entry));
        }
        catch
        {
            // A failed cache write only costs a future re-query.
        }
    }

    public void Clear(string solutionName)
    {
        try
        {
            var dir = Path.Combine(_rootDirectory, Sanitize(solutionName));
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Best effort.
        }
    }

    private string PathFor(string solutionName, string key) =>
        Path.Combine(_rootDirectory, Sanitize(solutionName), key + ".json");

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
