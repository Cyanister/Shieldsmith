using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shieldsmith.Core.Models;

namespace Shieldsmith.Core.Parsing;

/// <summary>
/// Reads canvas apps: the CanvasApp metadata in customizations.xml plus the
/// internals of each .msapp, which is itself a zip.
///
/// Two traps this parser handles deliberately, both seen in real exports:
///   * .msapp entry names use forward slashes in some apps and backslashes in
///     others, even from the same tenant. Every entry name is normalised.
///   * The msapp filename is a truncated schema name with its own disambiguator
///     and cannot be derived from the schema name, so files are enumerated.
/// </summary>
public static class CanvasAppParser
{
    public static void Parse(XDocument customizations, string rootPath, SolutionModel model)
    {
        var metadata = customizations.Root?.Element("CanvasApps")?.Elements("CanvasApp") ?? [];
        var apps = new List<CanvasAppModel>();

        foreach (var element in metadata)
        {
            var app = new CanvasAppModel
            {
                Name = element.Element("Name")?.Value ?? string.Empty,
                DisplayName = element.Element("DisplayName")?.Value ?? string.Empty,
                Description = element.Element("Description")?.Value ?? string.Empty,
                AppVersion = element.Element("AppVersion")?.Value ?? string.Empty,
                CreatedByClientVersion = element.Element("CreatedByClientVersion")?.Value ?? string.Empty,
                BackgroundColor = element.Element("BackgroundColor")?.Value ?? string.Empty,
            };
            if (int.TryParse(element.Element("CanvasAppType")?.Value, out var type))
                app.CanvasAppType = type;
            if (string.IsNullOrEmpty(app.DisplayName)) app.DisplayName = app.Name;
            apps.Add(app);
        }

        var msappFiles = FindMsappFiles(rootPath);

        foreach (var app in apps)
        {
            var file = MatchMsapp(msappFiles, app.Name);
            if (file is not null)
            {
                try
                {
                    ReadMsapp(file, app);
                    app.HasInternals = true;
                    msappFiles.Remove(file);
                }
                catch (Exception ex)
                {
                    model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning,
                        $"Canvas app '{app.DisplayName}' could not be read from its .msapp: {ex.Message}"));
                }
            }
            else
            {
                model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Info,
                    $"Canvas app '{app.DisplayName}' has no .msapp in this export; only its metadata is documented."));
            }
            model.CanvasApps.Add(app);
        }

        // A .msapp with no metadata block still deserves documenting.
        foreach (var orphan in msappFiles)
        {
            var app = new CanvasAppModel
            {
                Name = Path.GetFileNameWithoutExtension(orphan),
                DisplayName = Path.GetFileNameWithoutExtension(orphan),
            };
            try
            {
                ReadMsapp(orphan, app);
                app.HasInternals = true;
                if (string.IsNullOrEmpty(app.DisplayName)) app.DisplayName = app.Name;
                model.CanvasApps.Add(app);
            }
            catch (Exception ex)
            {
                model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning,
                    $"An unreferenced .msapp could not be read: {ex.Message}"));
            }
        }
    }

    private static List<string> FindMsappFiles(string rootPath)
    {
        var directory = Path.Combine(rootPath, "CanvasApps");
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.msapp", SearchOption.AllDirectories).ToList()
            : new List<string>();
    }

    /// <summary>
    /// The msapp filename is the schema name truncated to roughly 45 characters
    /// with a 5-digit disambiguator, so it is matched by longest common prefix
    /// rather than reconstructed.
    /// </summary>
    private static string? MatchMsapp(List<string> files, string schemaName)
    {
        if (files.Count == 0 || string.IsNullOrEmpty(schemaName)) return null;

        return files
            .Select(file => new
            {
                File = file,
                Prefix = CommonPrefixLength(
                    Path.GetFileNameWithoutExtension(file).Replace("_DocumentUri", string.Empty),
                    schemaName),
            })
            .Where(match => match.Prefix >= Math.Min(8, schemaName.Length))
            .OrderByDescending(match => match.Prefix)
            .Select(match => match.File)
            .FirstOrDefault();
    }

    private static int CommonPrefixLength(string a, string b)
    {
        var length = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < length && char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i])) i++;
        return i;
    }

    private static void ReadMsapp(string msappPath, CanvasAppModel app)
    {
        using var stream = new FileStream(msappPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        // Entry names differ between apps: some use '/', some '\'.
        var entries = archive.Entries.ToDictionary(
            e => e.FullName.Replace('\\', '/').TrimStart('/'),
            e => e,
            StringComparer.OrdinalIgnoreCase);

        var appFormulas = new List<string>();
        ReadHeader(entries, app);
        ReadProperties(entries, app);
        ReadDataSources(entries, app);
        ReadControls(entries, app, appFormulas);
        DeriveVariables(app, appFormulas);
    }

    private static void ReadHeader(Dictionary<string, ZipArchiveEntry> entries, CanvasAppModel app)
    {
        if (!entries.TryGetValue("Header.json", out var entry)) return;
        using var document = ReadJson(entry);
        var root = document.RootElement;

        app.DocumentVersion = GetString(root, "DocVersion") ?? string.Empty;

        // LastSavedDateTimeUTC is US format with no timezone suffix.
        var saved = GetString(root, "LastSavedDateTimeUTC");
        if (saved is not null && DateTime.TryParseExact(saved, "MM/dd/yyyy HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            app.LastSavedUtc = parsed;
        }
    }

    private static void ReadProperties(Dictionary<string, ZipArchiveEntry> entries, CanvasAppModel app)
    {
        if (!entries.TryGetValue("Properties.json", out var entry)) return;
        using var document = ReadJson(entry);
        var root = document.RootElement;

        // The key is "Id", not "ID": a case-sensitive match on "ID" finds nothing.
        if (string.IsNullOrEmpty(app.DisplayName))
            app.DisplayName = GetString(root, "Name") ?? app.Name;
        if (string.IsNullOrEmpty(app.Description))
            app.Description = GetString(root, "AppDescription") ?? string.Empty;
        app.FormFactor = GetString(root, "DocumentAppType") ?? string.Empty;

        if (root.TryGetProperty("ControlCount", out var counts)
            && counts.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in counts.EnumerateObject())
            {
                if (property.Value.TryGetInt32(out var count))
                    app.ControlCounts[property.Name] = count;
            }
        }
    }

    private static void ReadDataSources(Dictionary<string, ZipArchiveEntry> entries, CanvasAppModel app)
    {
        if (!entries.TryGetValue("References/DataSources.json", out var entry)) return;
        using var document = ReadJson(entry);
        if (!document.RootElement.TryGetProperty("DataSources", out var sources)
            || sources.ValueKind != JsonValueKind.Array)
            return;

        foreach (var source in sources.EnumerateArray())
        {
            var type = GetString(source, "Type") ?? string.Empty;
            // Most entries are option sets and views: noise for documentation.
            if (!type.Equals("NativeCDSDataSourceInfo", StringComparison.OrdinalIgnoreCase)) continue;

            app.DataSources.Add(new CanvasDataSourceModel
            {
                Name = GetString(source, "Name") ?? string.Empty,
                Type = type,
                LogicalName = GetString(source, "LogicalName") ?? string.Empty,
                EntitySetName = GetString(source, "EntitySetName") ?? string.Empty,
            });
        }
    }

    private static void ReadControls(Dictionary<string, ZipArchiveEntry> entries, CanvasAppModel app,
        List<string> appFormulas)
    {
        var controlEntries = entries
            .Where(kv => kv.Key.StartsWith("Controls/", StringComparison.OrdinalIgnoreCase)
                         && kv.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Value);

        foreach (var entry in controlEntries)
        {
            using var document = ReadJson(entry);
            if (!document.RootElement.TryGetProperty("TopParent", out var top)) continue;

            var template = GetString(top, "Template", "Name") ?? string.Empty;

            // Controls/1.json is the App object rather than a screen, but its
            // OnStart is where most global variables are actually declared, so
            // its formulas still feed variable discovery.
            if (template.Equals("appinfo", StringComparison.OrdinalIgnoreCase))
            {
                var appControl = ReadControl(top);
                appFormulas.AddRange(appControl.SelfAndDescendants()
                    .SelectMany(c => c.Properties.Values));
                continue;
            }

            if (!template.Equals("screen", StringComparison.OrdinalIgnoreCase)) continue;

            var screen = new CanvasScreenModel
            {
                Name = GetString(top, "Name") ?? string.Empty,
                Index = top.TryGetProperty("Index", out var index) && index.TryGetInt32(out var i) ? i : 0,
            };

            if (top.TryGetProperty("Children", out var children)
                && children.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in children.EnumerateArray())
                    screen.Controls.Add(ReadControl(child));
            }

            app.Screens.Add(screen);
        }

        app.Screens.Sort((a, b) => a.Index.CompareTo(b.Index));
    }

    private static CanvasControlModel ReadControl(JsonElement element)
    {
        var template = GetString(element, "Template", "Name") ?? string.Empty;
        var variant = GetString(element, "VariantName") ?? string.Empty;
        var templateId = GetString(element, "Template", "Id") ?? string.Empty;

        // The three container flavours share one template and differ only by
        // variant; components are identified by their template id.
        var type = template;
        if (template.Equals("groupContainer", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(variant))
            type = variant;
        if (templateId.Contains("appmagic/Component", StringComparison.OrdinalIgnoreCase))
            type = "component";

        var control = new CanvasControlModel
        {
            Name = GetString(element, "Name") ?? string.Empty,
            Type = type,
        };

        ReadRules(element, control);

        if (element.TryGetProperty("Children", out var children)
            && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
                control.Children.Add(ReadControl(child));
        }

        return control;
    }

    private static void ReadRules(JsonElement element, CanvasControlModel control)
    {
        void Add(JsonElement rule)
        {
            var property = GetString(rule, "Property");
            var script = GetString(rule, "InvariantScript");
            var provider = GetString(rule, "RuleProviderType");
            if (property is null || string.IsNullOrWhiteSpace(script)) return;
            // Only what the maker actually authored; template defaults are noise.
            if (!string.Equals(provider, "User", StringComparison.OrdinalIgnoreCase)) return;
            if (script is "false" or "true" && property is "Visible") return;
            control.Properties[property] = script;
        }

        if (element.TryGetProperty("Rules", out var rules) && rules.ValueKind == JsonValueKind.Array)
        {
            foreach (var rule in rules.EnumerateArray()) Add(rule);
        }

        // Modern container and PCF controls put some formulas here instead.
        if (element.TryGetProperty("DynamicProperties", out var dynamic)
            && dynamic.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in dynamic.EnumerateArray())
            {
                if (item.TryGetProperty("Rule", out var rule)) Add(rule);
            }
        }
    }

    // Power Fx is not parsed with a full AST here: these patterns pick up the
    // declarations that matter for documentation without taking a dependency
    // on the PowerFx parser. Matches inside string literals are possible, so
    // the result is presented as a derived list, not an authoritative one.
    private static readonly Regex SetPattern =
        new(@"\bSet\s*\(\s*([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private static readonly Regex CollectPattern =
        new(@"\b(?:Clear)?Collect\s*\(\s*([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    private static void DeriveVariables(CanvasAppModel app, List<string> appFormulas)
    {
        var variables = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var collections = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var formula in app.Screens
                     .SelectMany(s => s.AllControls())
                     .SelectMany(c => c.Properties.Values)
                     .Concat(appFormulas))
        {
            foreach (Match match in SetPattern.Matches(formula))
                variables.Add(match.Groups[1].Value);
            foreach (Match match in CollectPattern.Matches(formula))
                collections.Add(match.Groups[1].Value);
        }

        app.GlobalVariables.AddRange(variables);
        app.Collections.AddRange(collections);
    }

    private static JsonDocument ReadJson(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return JsonDocument.Parse(reader.ReadToEnd());
    }

    private static string? GetString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(segment, out current))
                return null;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }
}
