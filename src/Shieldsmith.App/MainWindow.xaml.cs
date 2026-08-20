using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Shieldsmith.Ai;
using Shieldsmith.Ai.Prompting;
using Shieldsmith.Ai.Providers;
using Shieldsmith.Core.Models;
using Shieldsmith.Core.Parsing;
using Shieldsmith.Outputs.Diagrams;
using Shieldsmith.Outputs.Visio;
using Shieldsmith.Outputs.Word;

namespace Shieldsmith.App;

public partial class MainWindow : Window
{
    private SolutionModel? _solution;
    private ErdResult? _erd;
    private DiagramSet? _diagrams;
    private string? _workDirectory;
    private string? _diagramDirectory;
    private readonly SecureKeyStore _keyStore = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            ReportEnvironment();
            if (App.StartupSolutionPath is not null)
            {
                SetZipPath(App.StartupSolutionPath);
                btnAnalyze_Click(this, new RoutedEventArgs());
            }
            else if (App.ScreenshotPath is not null)
            {
                // --shot with no solution: capture the empty state and exit.
                // The idle pass lets the environment pills lay out their text.
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                CaptureTo(App.ScreenshotPath);
                Application.Current.Shutdown();
            }
        };
    }

    /// <summary>
    /// Verification harness behind --shot: writes one PNG per results tab and
    /// exits. The interface is the deliverable here, so it is checked by
    /// looking at it rather than by trusting that the XAML compiled.
    /// </summary>
    private async Task CaptureEveryTabAsync(string basePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(basePath))!;
        var stem = Path.GetFileNameWithoutExtension(basePath);

        foreach (var tab in tabResults.Items.OfType<TabItem>().ToList())
        {
            tabResults.SelectedItem = tab;
            // Two idle passes: one to lay the tab out, one to let the diagram
            // fit itself to the viewport it has only just been given.
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            if (ReferenceEquals(tab.Content, null) is false) FitDiagram();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

            var name = tab.Header?.ToString()?.ToLowerInvariant().Replace(' ', '-') ?? "tab";
            CaptureTo(Path.Combine(directory, $"{stem}-{name}.png"));

            // The diagram pane shows one image at a time, so walk them all.
            if (!ReferenceEquals(tab, tabResults.Items.OfType<TabItem>()
                    .FirstOrDefault(t => (t.Header as string) == "Diagrams"))) continue;

            for (var i = 1; i < cmbDiagram.Items.Count; i++)
            {
                cmbDiagram.SelectedIndex = i;
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                FitDiagram();
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                CaptureTo(Path.Combine(directory, $"{stem}-diagram-{i}.png"));
            }
            if (cmbDiagram.Items.Count > 0) cmbDiagram.SelectedIndex = 0;
        }

        var about = new AboutWindow(_solution) { Owner = this };
        about.Show();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        CaptureWindowTo(about, Path.Combine(directory, $"{stem}-about.png"));
        about.Close();

        // The consent dialogue is the surface that decides whether anything
        // leaves the machine, so it is checked by eye like everything else.
        if (_solution is not null)
        {
            var consent = new ConsentDialog("Claude", "claude-opus-5",
                PromptBuilder.BuildPayload(_solution, redactEnvironmentValues: true)) { Owner = this };
            consent.Show();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            CaptureWindowTo(consent, Path.Combine(directory, $"{stem}-consent.png"));
            consent.Close();
        }

        Application.Current.Shutdown();
    }

    /// <summary>
    /// Renders the live window to a PNG. WPF draws its own visual tree, so this
    /// captures what the user sees whether or not the window is on top.
    /// </summary>
    private void CaptureTo(string path) => CaptureWindowTo(this, path);

    private static void CaptureWindowTo(Window window, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Round(window.ActualWidth * dpi.DpiScaleX),
            (int)Math.Round(window.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    // ---- Environment pills ------------------------------------------------

    /// <summary>
    /// States what this machine can actually do before the user asks for it.
    /// A missing engine is a supported state, not an error, so the pill goes
    /// grey rather than red.
    /// </summary>
    private void ReportEnvironment()
    {
        var webView = Shieldsmith.Mermaid.MermaidRenderer.RuntimeVersion;
        SetPill(pillMermaid, txtPillMermaid,
            webView is not null,
            webView is not null ? "Mermaid ready" : "Mermaid unavailable",
            webView is not null
                ? $"Rendering through WebView2 {webView}."
                : "The WebView2 runtime was not found. Shieldsmith's built-in engine will draw the diagrams.");

        var dot = GraphvizRunner.FindDotExecutable();
        SetPill(pillGraphviz, txtPillGraphviz, dot is not null,
            dot is not null ? "Graphviz found" : "Graphviz absent",
            dot ?? "Optional. Only used for the Visio layout when it is present.");

        var claude = ClaudeCodeCliProvider.FindCli();
        SetPill(pillClaude, txtPillClaude, claude is not null,
            claude is not null ? "Claude Code found" : "Claude Code absent",
            claude ?? "Install the Claude Code CLI to use it as an AI provider without an API key.");
    }

    private void SetPill(Border pill, TextBlock label, bool available, string text, string tooltip)
    {
        label.Text = text;
        label.Foreground = (Brush)FindResource(available ? "PrimaryDark" : "Subtle");
        pill.Background = (Brush)FindResource(available ? "TintLight" : "Canvas");
        pill.BorderBrush = (Brush)FindResource(available ? "TintStrong" : "Border");
        pill.ToolTip = tooltip;
    }

    // ---- Picking a solution ----------------------------------------------

    private void dropZone_Click(object sender, RoutedEventArgs e) => Browse();

    private void Browse()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a Power Platform solution export",
            Filter = "Solution export (*.zip)|*.zip|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() == true) SetZipPath(dialog.FileName);
    }

    private void SetZipPath(string path)
    {
        txtZipFilePath.Text = path;
        txtZipFilePath.Visibility = Visibility.Visible;
        txtDropTitle.Text = Path.GetFileName(path);
        txtDropHint.Text = "Click to choose a different export";
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = FirstDroppedZip(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        var zip = FirstDroppedZip(e);
        if (zip is null) return;
        SetZipPath(zip);
        e.Handled = true;
    }

    private static string? FirstDroppedZip(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
        return (e.Data.GetData(DataFormats.FileDrop) as string[])
            ?.FirstOrDefault(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(f));
    }

    // ---- AI settings ------------------------------------------------------

    private string SelectedProviderName => cmbAiProvider.SelectedIndex switch
    {
        1 => AnthropicProvider.ProviderName,
        2 => OpenAiProvider.ProviderName,
        3 => ClaudeCodeCliProvider.ProviderName,
        _ => string.Empty,
    };

    private void cmbAiProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (pnlApiKey is null) return; // fires during InitializeComponent
        var needsKey = cmbAiProvider.SelectedIndex is 1 or 2;
        var aiOn = cmbAiProvider.SelectedIndex > 0;
        pnlApiKey.Visibility = needsKey ? Visibility.Visible : Visibility.Collapsed;
        chkIncludeEnvValues.Visibility = aiOn ? Visibility.Visible : Visibility.Collapsed;

        txtAiStatus.Text = cmbAiProvider.SelectedIndex switch
        {
            1 or 2 => _keyStore.HasKey(SelectedProviderName)
                ? "A key is stored for this provider, encrypted with Windows DPAPI."
                : "No key stored yet. Enter your API key and press Save.",
            3 => ClaudeCodeCliProvider.FindCli() is not null
                ? "Claude Code CLI found. It uses your existing subscription, so no key is needed."
                : "Claude Code CLI not found on PATH.",
            _ => string.Empty,
        };
    }

    private void btnSaveKey_Click(object sender, RoutedEventArgs e)
    {
        if (cmbAiProvider.SelectedIndex is not (1 or 2)) return;
        _keyStore.SetKey(SelectedProviderName, pwdApiKey.Password);
        pwdApiKey.Clear();
        txtAiStatus.Text = _keyStore.HasKey(SelectedProviderName)
            ? "Key saved, encrypted with Windows DPAPI. It is never shown or logged."
            : "Key cleared.";
    }

    private IAiProvider? BuildProvider()
    {
        var model = string.IsNullOrWhiteSpace(txtAiModel.Text) ? null : txtAiModel.Text.Trim();
        switch (cmbAiProvider.SelectedIndex)
        {
            case 1:
                var anthropicKey = _keyStore.GetKey(AnthropicProvider.ProviderName);
                if (anthropicKey is null)
                {
                    MessageBox.Show("Save your Anthropic API key first.", "Shieldsmith",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return null;
                }
                return new AnthropicProvider(anthropicKey, model);
            case 2:
                var openAiKey = _keyStore.GetKey(OpenAiProvider.ProviderName);
                if (openAiKey is null)
                {
                    MessageBox.Show("Save your OpenAI API key first.", "Shieldsmith",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return null;
                }
                return new OpenAiProvider(openAiKey, model);
            case 3:
                return new ClaudeCodeCliProvider();
            default:
                return null;
        }
    }

    /// <summary>
    /// Runs the AI enrichment pass if a provider is selected and the user
    /// consents after seeing exactly what will be sent. Returns null (and the
    /// document stays complete) in every other case.
    /// </summary>
    private async Task<GeneratedInterpretation?> RunEnrichmentAsync()
    {
        if (_solution is null || cmbAiProvider.SelectedIndex <= 0) return null;
        var provider = BuildProvider();
        if (provider is null) return null;

        var redact = chkIncludeEnvValues.IsChecked != true;
        var payload = PromptBuilder.BuildPayload(_solution, redact);

        var consent = new ConsentDialog(provider.Name, provider.Model, payload) { Owner = this };
        if (consent.ShowDialog() != true)
        {
            txtAiStatus.Text = "AI interpretation skipped; nothing was sent.";
            return null;
        }

        txtStatus.Text = "Checking the AI provider...";
        var availability = await provider.CheckAvailabilityAsync();
        if (!availability.IsAvailable)
        {
            txtAiStatus.Text = $"AI provider unavailable: {availability.Detail} Continuing without AI.";
            return null;
        }
        txtAiStatus.Text = availability.Detail;

        var progress = new Progress<string>(message => txtStatus.Text = message);
        var service = new AiEnrichmentService(provider);
        var solution = _solution;
        var interpretation = await Task.Run(() => service.EnrichAsync(solution,
            new AiEnrichmentService.Options { RedactEnvironmentValues = redact }, progress));
        txtAiStatus.Text = "AI interpretation generated " +
                           $"({interpretation.TotalInputTokens} in / {interpretation.TotalOutputTokens} out tokens).";
        return interpretation;
    }

    // ---- Analysis ---------------------------------------------------------

    private async void btnAnalyze_Click(object sender, RoutedEventArgs e)
    {
        var zipPath = txtZipFilePath.Text;
        if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
        {
            MessageBox.Show("Select a solution export zip first.", "Shieldsmith",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SetBusy(true);
        var showAttributes = chkShowAttributes.IsChecked == true;
        var flowDiagrams = chkFlowDiagrams.IsChecked == true;
        var useMermaid = cmbEngine.SelectedIndex == 0;
        var progress = new Progress<string>(message => txtStatus.Text = message);

        try
        {
            CleanWorkDirectory();
            _workDirectory = Path.Combine(Path.GetTempPath(), "Shieldsmith", $"session-{Guid.NewGuid():N}");
            _diagramDirectory = Path.Combine(_workDirectory, "diagrams");

            var workDirectory = _workDirectory;
            var diagramDirectory = _diagramDirectory;

            (_solution, _erd, _diagrams) = await Task.Run(() =>
            {
                using var unpacked = SolutionUnpacker.Unpack(zipPath);
                var model = SolutionParser.Parse(unpacked, progress);

                var bridge = MermaidBridge.Create(useMermaid, out var mermaidStatus);
                ((IProgress<string>)progress).Report(mermaidStatus);

                var set = DiagramSuite.Build(model, diagramDirectory,
                    new DiagramSuite.Options
                    {
                        ShowColumns = showAttributes,
                        IncludeFlowDiagrams = flowDiagrams,
                        PreferredEngine = useMermaid ? DiagramEngine.Mermaid : DiagramEngine.Internal,
                    },
                    bridge, progress);
                set.LastMermaidMessage ??= MermaidBridge.LastMessage;

                // The Graphviz layout is still produced: the Visio writer needs
                // real coordinates, which only ErdGenerator computes.
                ((IProgress<string>)progress).Report("Laying out the data model for Visio...");
                var erd = ErdGenerator.Generate(model, workDirectory, model.UniqueName + "_erd", showAttributes);
                return (model, erd, set);
            });

            PopulateResults();

            var relationships = _solution.OneToManyRelationships.Count + _solution.ManyToManyRelationships.Count;
            txtStatus.Text = $"Analysed {_solution.DisplayName}: {_solution.Entities.Count} tables, " +
                             $"{relationships} relationships, {_solution.Processes.Count} automations, " +
                             $"{_diagrams.FlowDiagrams.Count + (_diagrams.Erd is null ? 0 : 1)} diagrams drawn.";

            btnGenerateWord.IsEnabled = true;
            btnGenerateMarkdown.IsEnabled = true;
            btnGenerateVisio.IsEnabled = true;
            btnExportPack.IsEnabled = true;
        }
        catch (SolutionFormatException ex)
        {
            txtStatus.Text = ex.Message;
            MessageBox.Show(ex.Message, "Not a solution export", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"Analysis failed: {ex.Message}";
            MessageBox.Show($"Analysis failed: {ex.Message}", "Shieldsmith", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            if (App.ScreenshotPath is not null) await CaptureEveryTabAsync(App.ScreenshotPath);
        }
    }

    private void SetBusy(bool busy)
    {
        btnAnalyze.IsEnabled = !busy;
        progressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Cursor = busy ? Cursors.Wait : Cursors.Arrow;
    }

    // ---- Results ----------------------------------------------------------

    private void PopulateResults()
    {
        if (_solution is null) return;

        emptyState.Visibility = Visibility.Collapsed;
        resultsPanel.Visibility = Visibility.Visible;

        txtSolutionName.Text = _solution.DisplayName;
        txtBadgeManaged.Text = _solution.IsManaged ? "Managed" : "Unmanaged";
        txtSolutionMeta.Text = $"{_solution.UniqueName}  ·  version {_solution.Version}  ·  " +
                               $"published by {_solution.PublisherDisplayName} (prefix {_solution.CustomizationPrefix})";

        statTables.Text = _solution.Entities.Count.ToString();
        statRelationships.Text = (_solution.OneToManyRelationships.Count +
                                  _solution.ManyToManyRelationships.Count).ToString();
        statFlows.Text = _solution.Processes.Count.ToString();
        statApps.Text = (_solution.CanvasApps.Count + _solution.AppModules.Count).ToString();
        statAgents.Text = _solution.Agents.Count.ToString();
        statComponents.Text = _solution.RootComponents.Count.ToString();

        dgComponents.ItemsSource = _solution.RootComponents;
        dgEntities.ItemsSource = _solution.Entities;
        dgEnvVars.ItemsSource = _solution.EnvironmentVariables;
        dgRelationships.ItemsSource = BuildRelationshipRows();
        dgProcesses.ItemsSource = BuildProcessRows();
        dgApps.ItemsSource = BuildAppRows();
        dgAgents.ItemsSource = BuildAgentRows();
        dgDiagnostics.ItemsSource = BuildNoteRows();

        PopulateDiagramPicker();
    }

    public sealed record RelationshipRow(string Name, string Kind, string One, string Many, string Via);
    public sealed record ProcessRow(string Name, string Kind, string Trigger, string Detail);
    // Canvas and model-driven apps share no meaningful measurements, so the
    // grid carries one summary column rather than columns that only apply to
    // half the rows.
    public sealed record AppRow(string Name, string Kind, string Detail);
    public sealed record AgentRow(string Name, string Topics, string Tools, string Knowledge, string Auth);
    public sealed record NoteRow(string Severity, string Message);

    private List<RelationshipRow> BuildRelationshipRows()
    {
        var rows = new List<RelationshipRow>();
        foreach (var r in _solution!.OneToManyRelationships)
            rows.Add(new RelationshipRow(r.SchemaName, "One to many", r.ReferencedEntity,
                r.ReferencingEntity, r.ReferencingAttribute));
        foreach (var r in _solution.ManyToManyRelationships)
            rows.Add(new RelationshipRow(r.SchemaName, "Many to many", r.Entity1, r.Entity2, r.IntersectEntity));
        // Inferred lookups are drawn dashed and listed separately everywhere
        // else, so they carry their own kind here rather than being mixed in.
        foreach (var l in _solution.InferredLookups)
            rows.Add(new RelationshipRow(l.AttributeDisplayName, "Inferred", l.AttributeType,
                l.Entity, l.AttributeLogicalName));
        return rows;
    }

    private List<ProcessRow> BuildProcessRows()
    {
        var rows = new List<ProcessRow>();
        foreach (var p in _solution!.Processes)
        {
            string trigger, detail;
            if (p.CloudFlow is not null)
            {
                trigger = p.CloudFlow.Trigger?.Summary ?? "No trigger read";
                detail = Count(p.CloudFlow.AllActions().Count(), "action") + ", " +
                         Count(p.CloudFlow.ConnectorsUsed.Count(), "connector");
            }
            else if (p.DesktopFlow is not null)
            {
                trigger = "Run on demand or from a cloud flow";
                var steps = p.DesktopFlow.Subflows.Sum(s => s.Steps.Count);
                detail = $"{Count(steps, "step")} in {Count(p.DesktopFlow.Subflows.Count, "subflow")}";
            }
            else
            {
                trigger = string.IsNullOrEmpty(p.PrimaryEntity) ? string.Empty : $"On {p.PrimaryEntity}";
                detail = p.IsActive ? "Active" : "Draft";
            }
            rows.Add(new ProcessRow(p.Name, Spaced(p.Kind.ToString()), trigger, detail));
        }
        return rows;
    }

    private List<AppRow> BuildAppRows()
    {
        var rows = new List<AppRow>();
        foreach (var a in _solution!.CanvasApps)
        {
            string detail;
            if (!a.HasInternals)
            {
                detail = "Metadata only: this export carries no .msapp to read.";
            }
            else
            {
                var controls = a.Screens.Sum(s => s.AllControls().Count());
                detail = string.Join(", ",
                    Count(a.Screens.Count, "screen"),
                    Count(controls, "control"),
                    Count(a.DataSources.Count, "table"),
                    Count(a.GlobalVariables.Count + a.Collections.Count, "variable"));
            }
            rows.Add(new AppRow(
                a.DisplayName.Length > 0 ? a.DisplayName : a.Name, a.KindDisplay, detail));
        }
        foreach (var a in _solution.AppModules)
        {
            var detail = Count(a.ComponentCount, "component");
            if (a.ClientType.Length > 0) detail += $", {a.ClientType}";
            rows.Add(new AppRow(a.Name.Length > 0 ? a.Name : a.UniqueName,
                "Model-driven app", detail));
        }
        return rows;
    }

    private List<AgentRow> BuildAgentRows() =>
        _solution!.Agents.Select(a => new AgentRow(
            a.Name.Length > 0 ? a.Name : a.SchemaName,
            a.Topics.Count.ToString(),
            a.Tools.Count.ToString(),
            a.KnowledgeSources.Count.ToString(),
            a.AuthenticationModeDisplay)).ToList();

    private List<NoteRow> BuildNoteRows()
    {
        var rows = _solution!.Diagnostics
            .Select(d => new NoteRow(d.Severity.ToString(), d.Message)).ToList();
        if (_diagrams is not null)
            rows.AddRange(_diagrams.Notes.Select(n => new NoteRow("Diagram", n)));
        // ErdGenerator now only computes the coordinates the Visio writer needs,
        // so its warning is about Visio, not about the diagram on screen.
        if (_erd?.Warning is not null)
            rows.Add(new NoteRow("Visio", "Visio layout: " + _erd.Warning));
        if (rows.Count == 0)
            rows.Add(new NoteRow("Information", "Nothing to flag. Every component parsed cleanly."));
        return rows;
    }

    /// <summary>"1 screen", "2 screens". Nouns here are all regular plurals.</summary>
    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    /// <summary>Turns an enum name such as CloudFlow into "Cloud flow".</summary>
    private static string Spaced(string pascalCase)
    {
        var text = string.Concat(pascalCase.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));
        return text;
    }

    // ---- Diagram preview --------------------------------------------------

    private sealed record DiagramChoice(string Label, DiagramImage? Image, string MermaidSource)
    {
        public override string ToString() => Label;
    }

    private void PopulateDiagramPicker()
    {
        if (_diagrams is null) return;
        var choices = new List<DiagramChoice>();

        if (_diagrams.Erd is not null || _diagrams.ErdMermaidSource.Length > 0)
            choices.Add(new DiagramChoice("Entity relationship diagram",
                _diagrams.Erd, _diagrams.ErdMermaidSource));

        foreach (var (name, source) in _diagrams.FlowMermaidSources)
        {
            _diagrams.FlowDiagrams.TryGetValue(name, out var image);
            choices.Add(new DiagramChoice(name, image, source));
        }

        cmbDiagram.ItemsSource = choices;
        cmbDiagram.SelectedIndex = choices.Count > 0 ? 0 : -1;
        var any = choices.Count > 0;
        btnCopyMermaid.IsEnabled = any;
        btnOpenDiagrams.IsEnabled = any;
    }

    private void cmbDiagram_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbDiagram.SelectedItem is not DiagramChoice choice) return;

        if (choice.Image?.PngPath is not null && File.Exists(choice.Image.PngPath))
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            // Load into memory so the temp file is not held open, which would
            // block the work directory being swept on close.
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(choice.Image.PngPath);
            bitmap.EndInit();
            imgDiagram.Source = bitmap;
            FitDiagram();
        }
        else
        {
            imgDiagram.Source = null;
        }

        var engine = choice.Image is null ? "not rendered" : choice.Image.Engine.ToString().ToLowerInvariant();
        txtStatus.Text = $"{choice.Label}: drawn by the {engine} engine.";
    }

    private void SetZoom(double scale)
    {
        diagramScale.ScaleX = scale;
        diagramScale.ScaleY = scale;
    }

    /// <summary>The point below which shrinking to fit stops being readable.</summary>
    private const double LegibleScale = 0.6;

    /// <summary>
    /// Shows the whole diagram when that still reads, and falls back to fitting
    /// the width and scrolling when it would not. An ER diagram is usually far
    /// taller than it is wide, and fitting both axes turns it into a postage
    /// stamp; a flow chart normally fits whole and should be shown whole.
    /// </summary>
    private void FitDiagram()
    {
        if (imgDiagram.Source is not BitmapSource bitmap) return;
        var width = diagramScroller.ViewportWidth - 28;
        var height = diagramScroller.ViewportHeight - 28;
        // Width, not PixelWidth. A Mermaid render is supersampled and can carry
        // its scale in the DPI metadata, so device pixels would misjudge it.
        if (width <= 0 || height <= 0 || bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            SetZoom(1);
            return;
        }

        // Never enlarge past 1:1: a small diagram blown up just looks blurred.
        var byWidth = Math.Min(1.0, width / bitmap.Width);
        var whole = Math.Min(byWidth, height / bitmap.Height);
        SetZoom(whole >= LegibleScale ? whole : byWidth);
    }

    private void btnZoomIn_Click(object sender, RoutedEventArgs e) =>
        SetZoom(Math.Min(4.0, diagramScale.ScaleX * 1.25));

    private void btnZoomOut_Click(object sender, RoutedEventArgs e) =>
        SetZoom(Math.Max(0.1, diagramScale.ScaleX / 1.25));

    private void btnZoomReset_Click(object sender, RoutedEventArgs e) => FitDiagram();

    private void btnCopyMermaid_Click(object sender, RoutedEventArgs e)
    {
        if (cmbDiagram.SelectedItem is not DiagramChoice choice || choice.MermaidSource.Length == 0)
        {
            txtStatus.Text = "No Mermaid source for that diagram.";
            return;
        }
        Clipboard.SetText(choice.MermaidSource);
        txtStatus.Text = $"Mermaid source for {choice.Label} copied to the clipboard.";
    }

    private void btnOpenDiagrams_Click(object sender, RoutedEventArgs e)
    {
        if (_diagramDirectory is null || !Directory.Exists(_diagramDirectory)) return;
        Open(_diagramDirectory);
    }

    // ---- Outputs ----------------------------------------------------------

    private async void btnGenerateWord_Click(object sender, RoutedEventArgs e)
    {
        if (_solution is null) return;

        var dialog = new SaveFileDialog
        {
            Title = "Save Word document",
            Filter = "Word document (*.docx)|*.docx",
            FileName = $"{_solution.UniqueName}_Documentation.docx",
        };
        if (dialog.ShowDialog() != true) return;

        await RunOutput(btnGenerateWord, "Word document", async () =>
        {
            var interpretation = await RunEnrichmentAsync();
            var solution = _solution;
            var erdPng = _erd?.PngPath;
            var diagrams = _diagrams;
            return await Task.Run(() => WordReportBuilder.Write(
                solution, dialog.FileName, erdPng, interpretation, diagrams));
        });
    }

    private async void btnGenerateMarkdown_Click(object sender, RoutedEventArgs e)
    {
        if (_solution is null) return;

        var dialog = new OpenFolderDialog { Title = "Choose where to write the Markdown set" };
        if (dialog.ShowDialog() != true) return;

        await RunOutput(btnGenerateMarkdown, "Markdown set", async () =>
        {
            var interpretation = await RunEnrichmentAsync();
            var solution = _solution;
            var erdPng = _erd?.PngPath;
            var diagrams = _diagrams;
            var target = Path.Combine(dialog.FolderName, solution!.UniqueName + "_markdown");
            return await Task.Run(() => Shieldsmith.Outputs.Markdown.MarkdownReportBuilder.Write(
                solution, target, erdPng, interpretation, diagrams));
        });
    }

    private async void btnGenerateVisio_Click(object sender, RoutedEventArgs e)
    {
        if (_solution is null || _erd is null) return;

        var dialog = new SaveFileDialog
        {
            Title = "Save Visio diagram",
            Filter = "Visio drawing (*.vsdx)|*.vsdx",
            FileName = $"{_solution.UniqueName}_DataModel.vsdx",
        };
        if (dialog.ShowDialog() != true) return;

        await RunOutput(btnGenerateVisio, "Visio diagram", async () =>
        {
            var solution = _solution;
            var layout = _erd!.Layout;
            var fileName = dialog.FileName;
            await Task.Run(() => VsdxPackageWriter.Write(solution, layout, fileName));
            return fileName;
        });
    }

    private async void btnExportPack_Click(object sender, RoutedEventArgs e)
    {
        if (_solution is null) return;

        var dialog = new OpenFolderDialog { Title = "Choose where to write the Claude Code docpack" };
        if (dialog.ShowDialog() != true) return;

        await RunOutput(btnExportPack, "Docpack", async () =>
        {
            var solution = _solution;
            var erdPng = _diagrams?.Erd?.PngPath ?? _erd?.PngPath;
            var folderName = dialog.FolderName;
            return await Task.Run(() =>
                Shieldsmith.ExportPack.ExportPackWriter.Write(solution, folderName, erdPng));
        });
    }

    private void btnAbout_Click(object sender, RoutedEventArgs e) =>
        new AboutWindow(_solution) { Owner = this }.ShowDialog();

    private void btnCopyMcp_Click(object sender, RoutedEventArgs e)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Shieldsmith.Mcp.exe");
        var command = $"claude mcp add shieldsmith -- \"{exe}\"";
        Clipboard.SetText(command);
        txtStatus.Text = File.Exists(exe)
            ? "MCP registration command copied. Paste it into a terminal to give Claude Code live access to any solution zip."
            : $"Command copied, but {exe} is not present in this build. Publish Shieldsmith.Mcp alongside the app first.";
    }

    /// <summary>
    /// One place for the write-then-offer-to-open dance, so every output
    /// button behaves the same on success and on failure.
    /// </summary>
    private async Task RunOutput(Button button, string what, Func<Task<string>> work)
    {
        button.IsEnabled = false;
        SetBusy(true);
        txtStatus.Text = $"Writing the {what.ToLowerInvariant()}...";
        try
        {
            var path = await work();
            txtStatus.Text = $"{what} written: {path}";
            if (MessageBox.Show($"{what} written. Open it now?", "Shieldsmith",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                Open(path);
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"{what} failed: {ex.Message}";
            MessageBox.Show($"{what} failed: {ex.Message}", "Shieldsmith",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            button.IsEnabled = true;
            SetBusy(false);
        }
    }

    private static void Open(string path) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });

    // ---- Lifetime ---------------------------------------------------------

    private void CleanWorkDirectory()
    {
        if (_workDirectory is null) return;
        try { Directory.Delete(_workDirectory, recursive: true); } catch { /* swept at next startup */ }
        _workDirectory = null;
        _diagramDirectory = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        CleanWorkDirectory();
        base.OnClosed(e);
    }
}
