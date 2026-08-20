using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Shieldsmith.Ai;

/// <summary>
/// Stores API keys encrypted with Windows DPAPI (current user scope plus
/// app-specific entropy) under %APPDATA%\Shieldsmith. Keys are never logged and
/// never written to any report.
/// </summary>
public sealed class SecureKeyStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Shieldsmith.KeyStore.v1");
    private readonly string _filePath;

    public SecureKeyStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Shieldsmith", "keys.json");
    }

    public void SetKey(string providerName, string apiKey)
    {
        var keys = Load();
        if (string.IsNullOrEmpty(apiKey))
        {
            keys.Remove(providerName);
        }
        else
        {
            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(apiKey), Entropy, DataProtectionScope.CurrentUser);
            keys[providerName] = Convert.ToBase64String(protectedBytes);
        }
        Save(keys);
    }

    public string? GetKey(string providerName)
    {
        var keys = Load();
        if (!keys.TryGetValue(providerName, out var protectedBase64)) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(protectedBase64), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException)
        {
            // Different user or machine: the stored key is unreadable, treat as absent.
            return null;
        }
    }

    public bool HasKey(string providerName) => GetKey(providerName) is not null;

    private Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(_filePath))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_filePath))
                       ?? new Dictionary<string, string>();
        }
        catch
        {
            // A corrupt store means keys must be re-entered; never crash over it.
        }
        return new Dictionary<string, string>();
    }

    private void Save(Dictionary<string, string> keys)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(keys));
    }
}
