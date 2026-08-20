using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shieldsmith.Core.Models;

namespace Shieldsmith.Core.Parsing;

/// <summary>
/// Reads Power Automate Desktop flows and their Robin scripts.
///
/// Two storage formats exist in the wild and a parser that handles only one
/// silently produces empty flows for the other:
///   * Newer exports put the Robin script inline in the Workflow's Definition
///     element, XML-escaped, with literal "\r\n" two-character sequences.
///   * Older exports leave Definition out entirely and put a base64 zip in the
///     sidecar JSON, whose script.robin entry is UTF-16 encoded.
/// </summary>
public static class DesktopFlowParser
{
    public static void Attach(XElement workflowElement, ProcessModel process,
        string rootPath, SolutionModel model)
    {
        if (int.TryParse(workflowElement.Element("UIFlowType")?.Value, out var uiFlowType))
            process.UiFlowType = uiFlowType;

        var detail = new DesktopFlowDetail
        {
            SchemaVersion = workflowElement.Element("SchemaVersion")?.Value ?? string.Empty,
        };

        var script = ReadInlineDefinition(workflowElement)
                     ?? ReadPackagedDefinition(process, rootPath, model);

        if (script is null)
        {
            model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Info,
                $"Desktop flow '{process.Name}' has no readable script in this export."));
            process.DesktopFlow = detail;
            return;
        }

        try
        {
            RobinScript.Parse(script, detail);
        }
        catch (Exception ex)
        {
            model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning,
                $"Desktop flow '{process.Name}' has a script that could not be parsed: {ex.Message}"));
        }

        process.DesktopFlow = detail;
    }

    /// <summary>Format A: the script sits inline, escaped, in Definition.</summary>
    private static string? ReadInlineDefinition(XElement workflowElement)
    {
        var raw = workflowElement.Element("Definition")?.Value;
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // XDocument has already resolved XML entities; what remains is Robin's
        // own escaping. The value is wrapped in literal quotes, and line breaks
        // are the two characters backslash-r backslash-n, not real newlines.
        var script = raw.Trim();
        if (script.Length >= 2 && script[0] == '"' && script[^1] == '"')
            script = script[1..^1];

        return script
            .Replace("\\r\\n", "\n")
            .Replace("\\n", "\n")
            .Replace("\\t", "\t")
            .Replace("\\\"", "\"")
            .Replace("\\'", "'")
            .Replace("\\\\", "\\");
    }

    /// <summary>Format B: base64 zip in the sidecar JSON, script.robin is UTF-16.</summary>
    private static string? ReadPackagedDefinition(ProcessModel process, string rootPath, SolutionModel model)
    {
        if (string.IsNullOrEmpty(process.FileName)) return null;
        var jsonPath = Path.Combine(rootPath,
            process.FileName.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(jsonPath)) return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
            if (!document.RootElement.TryGetProperty("properties", out var properties)
                || !properties.TryGetProperty("definition", out var definition)
                || !definition.TryGetProperty("package", out var package))
                return null;

            var base64 = package.GetString();
            if (string.IsNullOrWhiteSpace(base64)) return null;

            using var stream = new MemoryStream(Convert.FromBase64String(base64));
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Replace('\\', '/').EndsWith("script.robin", StringComparison.OrdinalIgnoreCase));
            if (entry is null) return null;

            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            var bytes = buffer.ToArray();

            // script.robin is UTF-16; decoding as UTF-8 yields NUL-interleaved text.
            return bytes.Length >= 2 && bytes[1] == 0x00
                ? Encoding.Unicode.GetString(bytes)
                : Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning,
                $"Desktop flow '{process.Name}' has a packaged script that could not be unpacked: {ex.Message}"));
            return null;
        }
    }
}

/// <summary>
/// A line-oriented reader for Robin, the Power Automate Desktop language. It
/// extracts what documentation needs: inputs, outputs, subflows, and the
/// ordered action steps with their nesting and output bindings.
/// </summary>
internal static class RobinScript
{
    private static readonly Regex ActionPattern =
        new(@"^[A-Za-z][A-Za-z0-9]*\.[A-Za-z]", RegexOptions.Compiled);
    private static readonly Regex OutputBindingPattern =
        new(@"(\w+)\s*=>\s*(\w+)", RegexOptions.Compiled);
    private static readonly Regex FunctionPattern =
        new(@"^FUNCTION\s+([^\s]+)(\s+GLOBAL)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex VariablePattern =
        new(@"^@(INPUT|OUTPUT)\s+([^\s:]+)\s*:\s*(\{.*\})?", RegexOptions.Compiled);

    public static void Parse(string script, DesktopFlowDetail detail)
    {
        var main = new DesktopFlowSubflow { Name = "Main" };
        detail.Subflows.Add(main);
        var current = main;
        var depth = 0;
        var inBlockComment = false;
        var modules = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in script.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (inBlockComment)
            {
                if (line.Contains("#/")) inBlockComment = false;
                continue;
            }
            if (line.StartsWith("/#", StringComparison.Ordinal)) { inBlockComment = true; continue; }
            if (line.StartsWith("#", StringComparison.Ordinal)) continue;
            if (line.StartsWith("**", StringComparison.Ordinal)) continue;
            if (line.StartsWith("@@", StringComparison.Ordinal)) continue;
            if (line.StartsWith("IMPORT ", StringComparison.OrdinalIgnoreCase)) continue;

            var variable = VariablePattern.Match(line);
            if (variable.Success)
            {
                var entry = new DesktopFlowVariable
                {
                    Name = variable.Groups[2].Value,
                    Type = ExtractQuoted(variable.Groups[3].Value, "Type"),
                    Description = ExtractQuoted(variable.Groups[3].Value, "Description"),
                };
                if (variable.Groups[1].Value.Equals("INPUT", StringComparison.OrdinalIgnoreCase))
                    detail.Inputs.Add(entry);
                else
                    detail.Outputs.Add(entry);
                continue;
            }

            var function = FunctionPattern.Match(line);
            if (function.Success)
            {
                current = new DesktopFlowSubflow
                {
                    Name = function.Groups[1].Value.Trim('\'', '"'),
                    IsGlobal = function.Groups[2].Success,
                };
                detail.Subflows.Add(current);
                depth = 0;
                continue;
            }

            if (line.StartsWith("END FUNCTION", StringComparison.OrdinalIgnoreCase))
            {
                current = main;
                depth = 0;
                continue;
            }

            // Block structure drives the indent level shown in documentation.
            if (line.Equals("END", StringComparison.OrdinalIgnoreCase))
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }
            if (line.StartsWith("ELSE", StringComparison.OrdinalIgnoreCase)) continue;

            var opensBlock = line.StartsWith("IF ", StringComparison.OrdinalIgnoreCase)
                             || line.StartsWith("LOOP", StringComparison.OrdinalIgnoreCase)
                             || line.StartsWith("ON ERROR", StringComparison.OrdinalIgnoreCase)
                             || line.StartsWith("BLOCK", StringComparison.OrdinalIgnoreCase)
                             || line.StartsWith("ON BLOCK ERROR", StringComparison.OrdinalIgnoreCase);

            var body = line;
            var disabled = false;
            if (body.StartsWith("DISABLE ", StringComparison.OrdinalIgnoreCase))
            {
                disabled = true;
                body = body[8..].Trim();
            }

            if (ActionPattern.IsMatch(body))
            {
                var actionToken = body.Split(' ', 2)[0];
                var step = new DesktopFlowStep
                {
                    Action = actionToken,
                    Module = actionToken.Split('.')[0],
                    Depth = depth,
                    IsDisabled = disabled,
                };
                foreach (Match match in OutputBindingPattern.Matches(body))
                    step.OutputVariables.Add(match.Groups[2].Value);
                current.Steps.Add(step);

                // System and Flow are pseudo-modules, not real capabilities.
                if (step.Module is not ("System" or "Flow")) modules.Add(step.Module);
            }
            else if (body.StartsWith("CALL ", StringComparison.OrdinalIgnoreCase))
            {
                current.Steps.Add(new DesktopFlowStep
                {
                    Action = body, Module = "Flow", Depth = depth, IsDisabled = disabled,
                });
            }

            if (opensBlock) depth++;
        }

        detail.Modules.AddRange(modules);
    }

    private static string ExtractQuoted(string json, string key)
    {
        if (string.IsNullOrEmpty(json)) return string.Empty;
        var match = Regex.Match(json, $@"'{Regex.Escape(key)}'\s*:\s*'([^']*)'");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }
}
