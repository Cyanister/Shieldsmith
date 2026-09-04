using Shieldsmith.Core.Export;
using Shieldsmith.Core.Parsing;
using Shieldsmith.Outputs.Diagrams;

if (args.Length == 0)
{
    Console.WriteLine("Shieldsmith: offline Power Platform solution documentation.");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  shieldsmith generate <solution.zip> -o <dir> [--word] [--markdown] [--erd] [--vsdx] [--json]");
    Console.WriteLine("                                         Write everything requested to one folder");
    Console.WriteLine("                                         (no flags = all outputs)");
    Console.WriteLine("       AI options: --ai anthropic|openai|claude-code   add labelled AI interpretation");
    Console.WriteLine("                   --ai-model <model>                  override the provider's model");
    Console.WriteLine("                   --include-env-values                send env var values (default: redacted)");
    Console.WriteLine("                   Keys come from ANTHROPIC_API_KEY / OPENAI_API_KEY. Passing --ai");
    Console.WriteLine("                   is your consent to send the extracted structure to that provider.");
    Console.WriteLine("  shieldsmith export-pack <solution.zip> [outdir]");
    Console.WriteLine("                                         Write a docpack folder for use in Claude Code");
    Console.WriteLine("  shieldsmith analyze <solution.zip>          Print a summary of the solution");
    Console.WriteLine("  shieldsmith json <solution.zip> [out.json]  Write the canonical JSON extraction");
    Console.WriteLine("  shieldsmith diagram <solution.zip> [outdir] [--no-mermaid] [--no-flows]");
    Console.WriteLine("                                         Render the ERD and a chart per cloud flow");
    Console.WriteLine("  shieldsmith erd <solution.zip> [outdir]     Render the entity relationship diagram");
    Console.WriteLine("  shieldsmith vsdx <solution.zip> [out.vsdx]  Write a Visio diagram of the data model");
    Console.WriteLine("  shieldsmith word <solution.zip> [out.docx]  Write the Word documentation");
    return 1;
}

var command = args[0].ToLowerInvariant();

if (command == "check")
{
    Console.WriteLine("Shieldsmith environment check");
    Console.WriteLine();

    var graphviz = Shieldsmith.Outputs.Diagrams.GraphvizRunner.FindDotExecutable();
    Console.WriteLine($"  Graphviz          {(graphviz is null
        ? "not found (optional; install: winget install Graphviz.Graphviz)"
        : graphviz)}");

    Console.WriteLine("  Internal engine   available (always)");

    var webView = Shieldsmith.Mermaid.MermaidRenderer.RuntimeVersion;
    Console.WriteLine($"  WebView2 runtime  {webView ?? "not found"}");

    if (webView is not null)
    {
        var probe = new Shieldsmith.Mermaid.MermaidRenderer();
        var probeDir = Path.Combine(Path.GetTempPath(), "Shieldsmith", "check-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = probe.Render(
                "flowchart TD\n    a[\"Start\"] --> b[\"Finish\"]\n", probeDir, "probe");
            Console.WriteLine(result.Success
                ? $"  Mermaid render    working ({result.Width:0} x {result.Height:0} px)"
                : $"  Mermaid render    FAILED: {result.Message}");
        }
        finally
        {
            try { Directory.Delete(probeDir, recursive: true); } catch { /* ignore */ }
        }
    }

    var claude = Shieldsmith.Ai.Providers.ClaudeCodeCliProvider.FindCli();
    Console.WriteLine($"  Claude Code CLI   {claude ?? "not found (optional)"}");
    Console.WriteLine($"  ANTHROPIC_API_KEY {(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") is { Length: > 0 } ? "set" : "not set")}");
    Console.WriteLine($"  OPENAI_API_KEY    {(Environment.GetEnvironmentVariable("OPENAI_API_KEY") is { Length: > 0 } ? "set" : "not set")}");
    return 0;
}

if (command == "validate-docx")
{
    if (args.Length < 2 || !File.Exists(args[1]))
    {
        Console.Error.WriteLine("Give the path to a .docx file.");
        return 1;
    }
    using var wordDocument = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(args[1], false);
    var validationErrors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator()
        .Validate(wordDocument).ToList();
    Console.WriteLine($"Validation errors: {validationErrors.Count}");
    foreach (var error in validationErrors.Take(10))
        Console.WriteLine($"  {error.Description} ({error.Path?.XPath})");
    return validationErrors.Count == 0 ? 0 : 3;
}

var zipPath = args.Length > 1 ? args[1] : null;
if (zipPath is null || !File.Exists(zipPath))
{
    Console.Error.WriteLine("Give the path to a solution export zip.");
    return 1;
}

SolutionUnpacker.SweepOrphans();

try
{
    using var unpacked = SolutionUnpacker.Unpack(zipPath);
    var model = SolutionParser.Parse(unpacked, new Progress<string>(s => Console.Error.WriteLine(s)));

    switch (command)
    {
        case "analyze":
            Console.WriteLine($"Solution:     {model.DisplayName} ({model.UniqueName}) v{model.Version} " +
                              (model.IsManaged ? "[managed]" : "[unmanaged]"));
            Console.WriteLine($"Publisher:    {model.PublisherDisplayName} (prefix '{model.CustomizationPrefix}')");
            Console.WriteLine($"Components:   {model.RootComponents.Count}");
            foreach (var group in model.RootComponents.GroupBy(c => c.TypeName).OrderByDescending(g => g.Count()))
                Console.WriteLine($"  {group.Count(),4} x {group.Key}");
            Console.WriteLine($"Entities:     {model.Entities.Count}");
            foreach (var entity in model.Entities)
                Console.WriteLine($"  {entity.DisplayName} ({entity.LogicalName}): {entity.Attributes.Count} columns, " +
                                  $"PK {entity.PrimaryIdAttribute?.LogicalName ?? "?"}, name {entity.PrimaryNameAttribute?.LogicalName ?? "?"}");
            Console.WriteLine($"Relationships: {model.OneToManyRelationships.Count} one-to-many, " +
                              $"{model.ManyToManyRelationships.Count} many-to-many, " +
                              $"{model.InferredLookups.Count} inferred lookups");
            if (model.CanvasApps.Count > 0)
            {
                Console.WriteLine($"Canvas apps:  {model.CanvasApps.Count}");
                foreach (var app in model.CanvasApps)
                    Console.WriteLine($"  {app.DisplayName} [{app.KindDisplay}]: " +
                                      (app.HasInternals
                                          ? $"{app.Screens.Count} screens, {app.TotalControls} controls, " +
                                            $"{app.DataSources.Count} tables, {app.GlobalVariables.Count} variables"
                                          : "metadata only, no .msapp in this export"));
            }
            if (model.Agents.Count > 0)
            {
                Console.WriteLine($"Agents:       {model.Agents.Count}");
                foreach (var agent in model.Agents)
                    Console.WriteLine($"  {agent.Name}: {agent.Topics.Count} topics, " +
                                      $"{agent.Tools.Count} tools, {agent.KnowledgeSources.Count} knowledge sources");
            }
            var desktopFlows = model.Processes.Where(p => p.DesktopFlow is not null).ToList();
            if (desktopFlows.Count > 0)
            {
                Console.WriteLine($"Desktop flows: {desktopFlows.Count}");
                foreach (var flow in desktopFlows)
                    Console.WriteLine($"  {flow.Name}: {flow.DesktopFlow!.TotalSteps} steps in " +
                                      $"{flow.DesktopFlow.Subflows.Count} subflows, " +
                                      $"modules {string.Join(", ", flow.DesktopFlow.Modules)}");
            }
            var businessProcessFlows = model.Processes
                .Where(p => p.BusinessProcessFlow is not null).ToList();
            if (businessProcessFlows.Count > 0)
            {
                Console.WriteLine($"Business process flows: {businessProcessFlows.Count}");
                foreach (var bpf in businessProcessFlows)
                    Console.WriteLine($"  {bpf.Name} on {bpf.PrimaryEntity}: " +
                                      $"{bpf.BusinessProcessFlow!.Stages.Count} stages " +
                                      $"({string.Join(" > ", bpf.BusinessProcessFlow.Stages.Select(s => s.Name))})");
            }

            if (model.PluginAssemblies.Count > 0 || model.SdkMessageSteps.Count > 0)
            {
                Console.WriteLine($"Plugins:      {model.PluginAssemblies.Count} assemblies, " +
                                  $"{model.SdkMessageSteps.Count} registered steps");
                foreach (var assembly in model.PluginAssemblies)
                    Console.WriteLine($"  {assembly.Name} v{assembly.Version} [{assembly.IsolationMode}]: " +
                                      $"{assembly.Types.Count} types, {assembly.Steps.Count} steps");
                foreach (var step in model.SdkMessageSteps)
                    Console.WriteLine($"    {step.Summary}");
            }

            Console.WriteLine($"Environment variables: {model.EnvironmentVariables.Count}");
            foreach (var variable in model.EnvironmentVariables)
                Console.WriteLine($"  {variable.SchemaName} ({variable.TypeName})" +
                                  (variable.HasCurrentValue ? " [has value]" : string.Empty));
            if (model.Diagnostics.Count > 0)
            {
                Console.WriteLine("Diagnostics:");
                foreach (var diagnostic in model.Diagnostics)
                    Console.WriteLine($"  {diagnostic}");
            }
            return 0;

        case "json":
            var json = SolutionJsonExporter.ToJson(model);
            var outPath = args.Length > 2 ? args[2] : Path.ChangeExtension(zipPath, ".json");
            File.WriteAllText(outPath, json);
            Console.WriteLine($"Wrote {outPath}");
            return 0;

        case "erd":
            var outDir = args.Length > 2 ? args[2] : Path.GetDirectoryName(Path.GetFullPath(zipPath))!;
            var erd = Shieldsmith.Outputs.Diagrams.ErdGenerator.Generate(
                model, outDir, Path.GetFileNameWithoutExtension(zipPath) + "_erd");
            Console.WriteLine($"DOT: {erd.DotPath}");
            Console.WriteLine($"PNG: {erd.PngPath}");
            if (erd.SvgPath is not null) Console.WriteLine($"SVG: {erd.SvgPath}");
            Console.WriteLine(erd.UsedGraphviz ? "Layout: Graphviz" : "Layout: built-in fallback");
            if (erd.Warning is not null) Console.WriteLine($"Note: {erd.Warning}");
            return 0;

        case "vsdx":
            var runner = new Shieldsmith.Outputs.Diagrams.GraphvizRunner();
            Shieldsmith.Outputs.Diagrams.ErdLayout layout;
            if (runner.IsAvailable)
            {
                var dotPath = Path.Combine(Path.GetTempPath(), $"shieldsmith-{Guid.NewGuid():N}.dot");
                File.WriteAllText(dotPath, Shieldsmith.Outputs.Diagrams.DotBuilder.Build(model));
                try { layout = Shieldsmith.Outputs.Diagrams.DotPlainLayoutReader.Parse(runner.RenderPlain(dotPath)); }
                finally { File.Delete(dotPath); }
            }
            else
            {
                layout = Shieldsmith.Outputs.Diagrams.FallbackLayoutEngine.Layout(model, showAttributes: true);
            }
            var vsdxPath = args.Length > 2 ? args[2] : Path.ChangeExtension(zipPath, ".vsdx");
            Shieldsmith.Outputs.Visio.VsdxPackageWriter.Write(model, layout, vsdxPath);
            Console.WriteLine($"Wrote {vsdxPath}");
            return 0;

        case "generate":
        {
            var flags = args.Skip(2).Where(a => a.StartsWith("--")).Select(a => a.ToLowerInvariant()).ToHashSet();
            var outputIndex = Array.FindIndex(args, a => a is "-o" or "--output");
            var outputDir = outputIndex >= 0 && outputIndex + 1 < args.Length
                ? args[outputIndex + 1]
                : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(zipPath))!, model.UniqueName + "_docs");
            string? ValueOf(string flag)
            {
                var index = Array.FindIndex(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
                return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
            }

            var outputFlags = new[] { "--word", "--markdown", "--erd", "--vsdx", "--json" };
            var all = !flags.Overlaps(outputFlags);
            Directory.CreateDirectory(outputDir);

            Shieldsmith.Core.Models.GeneratedInterpretation? interpretation = null;
            var aiChoice = ValueOf("--ai")?.ToLowerInvariant();
            if (aiChoice is not null)
            {
                var aiModel = ValueOf("--ai-model");
                Shieldsmith.Ai.IAiProvider? provider = aiChoice switch
                {
                    "anthropic" when Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") is { Length: > 0 } key =>
                        new Shieldsmith.Ai.Providers.AnthropicProvider(key, aiModel),
                    "openai" when Environment.GetEnvironmentVariable("OPENAI_API_KEY") is { Length: > 0 } key =>
                        new Shieldsmith.Ai.Providers.OpenAiProvider(key, aiModel),
                    "claude-code" => new Shieldsmith.Ai.Providers.ClaudeCodeCliProvider(),
                    "anthropic" or "openai" => null,
                    _ => null,
                };
                if (provider is null)
                {
                    Console.Error.WriteLine(aiChoice is "anthropic" or "openai"
                        ? $"--ai {aiChoice} needs the {(aiChoice == "anthropic" ? "ANTHROPIC_API_KEY" : "OPENAI_API_KEY")} environment variable."
                        : $"Unknown AI provider '{aiChoice}'. Use anthropic, openai or claude-code.");
                    return 1;
                }

                var availability = await provider.CheckAvailabilityAsync();
                if (!availability.IsAvailable)
                {
                    Console.Error.WriteLine($"AI provider unavailable: {availability.Detail}");
                    Console.Error.WriteLine("Continuing without AI interpretation.");
                }
                else
                {
                    Console.Error.WriteLine(availability.Detail);
                    var service = new Shieldsmith.Ai.Prompting.AiEnrichmentService(provider);
                    interpretation = await service.EnrichAsync(model,
                        new Shieldsmith.Ai.Prompting.AiEnrichmentService.Options
                        {
                            RedactEnvironmentValues = !flags.Contains("--include-env-values"),
                        },
                        new Progress<string>(s => Console.Error.WriteLine(s)));
                    Console.Error.WriteLine(
                        $"AI enrichment done ({interpretation.TotalInputTokens} in / {interpretation.TotalOutputTokens} out tokens).");
                }
            }

            // The full diagram suite: Shieldsmith's own engine by default, plus a
            // chart per cloud flow. Mermaid is opt-in via --mermaid.
            var useMermaid = !flags.Contains("--no-mermaid");
            var diagramBridge = Shieldsmith.Cli.MermaidBridge.Create(useMermaid, out var diagramMermaidStatus);
            Console.Error.WriteLine(diagramMermaidStatus);
            var diagramSet = DiagramSuite.Build(model, Path.Combine(outputDir, "diagrams"),
                new DiagramSuite.Options
                {
                    IncludeFlowDiagrams = !flags.Contains("--no-flows"),
                    PreferredEngine = useMermaid ? DiagramEngine.Mermaid : DiagramEngine.Internal,
                },
                diagramBridge,
                new Progress<string>(s => Console.Error.WriteLine(s)));

            // Graphviz path kept for VSDX coordinates and the --erd files.
            var generatedErd = Shieldsmith.Outputs.Diagrams.ErdGenerator.Generate(model, outputDir, "erd");
            if (generatedErd.Warning is not null) Console.Error.WriteLine(generatedErd.Warning);

            if (all || flags.Contains("--word"))
                Console.WriteLine("Word:     " + Shieldsmith.Outputs.Word.WordReportBuilder.Write(
                    model, Path.Combine(outputDir, model.UniqueName + "_Documentation.docx"), generatedErd.PngPath,
                    interpretation, diagramSet));
            if (all || flags.Contains("--markdown"))
                Console.WriteLine("Markdown: " + Shieldsmith.Outputs.Markdown.MarkdownReportBuilder.Write(
                    model, Path.Combine(outputDir, "markdown"), generatedErd.PngPath, interpretation, diagramSet));
            if (all || flags.Contains("--vsdx"))
            {
                var generatedVsdx = Path.Combine(outputDir, model.UniqueName + "_DataModel.vsdx");
                Shieldsmith.Outputs.Visio.VsdxPackageWriter.Write(model, generatedErd.Layout, generatedVsdx);
                Console.WriteLine("Visio:    " + generatedVsdx);
            }
            if (all || flags.Contains("--json"))
            {
                var jsonOut = Path.Combine(outputDir, model.UniqueName + ".json");
                File.WriteAllText(jsonOut, SolutionJsonExporter.ToJson(model));
                Console.WriteLine("JSON:     " + jsonOut);
            }
            if (all || flags.Contains("--erd"))
                Console.WriteLine($"ERD:      {generatedErd.PngPath} ({(generatedErd.UsedGraphviz ? "Graphviz" : "built-in layout")})");
            return 0;
        }

        case "diagram":
        {
            var diagramFlags = args.Skip(2).Where(a => a.StartsWith("--"))
                .Select(a => a.ToLowerInvariant()).ToHashSet();
            var diagramDir = args.Length > 2 && !args[2].StartsWith("--")
                ? args[2]
                : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(zipPath))!,
                    model.UniqueName + "_diagrams");

            var diagramUseMermaid = !diagramFlags.Contains("--no-mermaid");
            var bridge = Shieldsmith.Cli.MermaidBridge.Create(diagramUseMermaid, out var mermaidStatus);
            Console.Error.WriteLine(mermaidStatus);

            var set = DiagramSuite.Build(model, diagramDir,
                new DiagramSuite.Options
                {
                    IncludeFlowDiagrams = !diagramFlags.Contains("--no-flows"),
                    PreferredEngine = diagramUseMermaid ? DiagramEngine.Mermaid : DiagramEngine.Internal,
                },
                bridge,
                new Progress<string>(s => Console.Error.WriteLine(s)));
            set.LastMermaidMessage ??= Shieldsmith.Cli.MermaidBridge.LastMessage;

            if (set.Erd is not null)
                Console.WriteLine($"ERD:   {set.Erd.SvgPath}  [{set.Erd.Engine}]");
            Console.WriteLine($"Flow charts: {set.FlowDiagrams.Count} rendered, " +
                              $"{set.FlowMermaidSources.Count} Mermaid sources.");
            foreach (var note in set.Notes) Console.WriteLine($"Note: {note}");
            return 0;
        }

        case "export-pack":
        {
            var packOutputDir = args.Length > 2 && !args[2].StartsWith("--")
                ? args[2]
                : Path.GetDirectoryName(Path.GetFullPath(zipPath))!;
            var packErd = Shieldsmith.Outputs.Diagrams.ErdGenerator.Generate(
                model, Path.Combine(Path.GetTempPath(), "Shieldsmith", $"pack-{Guid.NewGuid():N}"), "erd");
            var packDir = Shieldsmith.ExportPack.ExportPackWriter.Write(model, packOutputDir, packErd.PngPath);
            Console.WriteLine($"Docpack: {packDir}");
            Console.WriteLine("Open that folder in a Claude Code terminal and ask questions about the solution.");
            return 0;
        }

        case "word":
            var workDir = Path.Combine(Path.GetTempPath(), "Shieldsmith", $"word-{Guid.NewGuid():N}");
            try
            {
                var wordErd = Shieldsmith.Outputs.Diagrams.ErdGenerator.Generate(
                    model, workDir, model.UniqueName + "_erd");
                if (wordErd.Warning is not null) Console.Error.WriteLine(wordErd.Warning);
                var docxPath = args.Length > 2 ? args[2] : Path.ChangeExtension(zipPath, ".docx");
                Shieldsmith.Outputs.Word.WordReportBuilder.Write(model, docxPath, wordErd.PngPath);
                Console.WriteLine($"Wrote {docxPath}");
            }
            finally
            {
                try { Directory.Delete(workDir, recursive: true); } catch { /* swept later */ }
            }
            return 0;

        default:
            Console.Error.WriteLine($"Unknown command '{command}'.");
            return 1;
    }
}
catch (SolutionFormatException ex)
{
    Console.Error.WriteLine($"Not a readable solution export: {ex.Message}");
    return 2;
}
