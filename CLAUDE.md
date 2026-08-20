# Shieldsmith

Cameron's personal product (not Valto): offline documentation for Power Platform solution
exports. Named "PowerPlatformDocumenter", then "DocCam", now Shieldsmith. The full product
plan, competitive analysis against PowerDocu, and phase history live in the repo history and
the workspace plan notes.

The repo folder is still `solutions/powerplatform-documenter`, which is accurate enough to
leave alone. Everything inside it is Shieldsmith.

`CHANGELOG.md` holds the decisions that stand, the phase history with commits, what has been
verified and how, and what is still open. Read it before reopening a settled decision.

## Ground rules

- **Read the definition, not the documentation.** Every parser here was written against real
  export XML, not Microsoft docs. When extending a parser, inspect a real export first
  (`data/ContosoTravel_sample/` is the unzipped fixture; large real managed solutions live under
  the surrounding workspace and must never enter this repo).
- **No client data in this repo, and no client names either.** The fixture is synthetic
  (ContosoTravel). Screenshots and test outputs for anything public come from the fixture only.
  Real test solutions are referred to by shape, never by client: this repo may yet be published,
  and a client name in a tracked file is a leak whether or not the data came with it. The paths
  to the real ones are kept in session memory, outside the repo.
- **Facts and inference never mix.** Inferred lookup relationships are modelled separately
  (`InferredLookups`) and always labelled inferred in every output. Parser gaps become
  `Diagnostics`, which are printed in the Word report; nothing is silently invented.
- **The document must remain complete with AI off.** AI output goes in visibly distinct labelled
  blocks only (`AppendAiBlock` in the Word and Markdown writers), never in a fact table, and
  never to fill a parser gap. Nothing leaves the machine without the consent dialogue; env var
  values are redacted by default.

## Architecture

- `Shieldsmith.Core` (net10.0): unpacker (zip-slip guarded, share-read, disposable temp dirs),
  `SolutionParser` + `FlowParser` + `UxParser`, object model, `SolutionJsonExporter` (the
  canonical JSON every downstream consumer shares), `GeneratedInterpretation` (the AI DTO, kept
  here so Outputs needs no AI dependency), `IEditionGate` (the only licensing seam; distribution
  model undecided).
- `Shieldsmith.Outputs` (net10.0-windows): `DotBuilder` + `GraphvizRunner` (detects dot.exe; absence
  is a supported state) + `FallbackLayoutEngine`/`FallbackErdRenderer`, `WordReportBuilder`
  (validates clean under OpenXmlValidator), `MarkdownReportBuilder`, `VsdxPackageWriter` (real
  OPC package).
- `Shieldsmith.Ai` (net10.0-windows): `IAiProvider` with `AnthropicProvider` (official Anthropic SDK),
  `OpenAiProvider` (official OpenAI SDK) and `ClaudeCodeCliProvider` (`claude -p --output-format
  json`; any parse failure means unavailable, never a crash). `SecureKeyStore` is DPAPI.
  `PromptBuilder` owns every prompt and the redaction rules; `AiCache` keys on
  SHA-256(prompt+provider+model+`PromptVersion`) so bump `PromptVersion` when prompts change.
- `Shieldsmith.ExportPack`: the docpack for the user's own Claude Code terminal.
- `Shieldsmith.Diagrams` (net10.0-windows): the built-in layered layout engine (Sugiyama: cycle
  removal, longest-path layering, dummy-node normalisation, median crossing reduction,
  coordinate assignment, polyline routing) with SVG and PNG renderers. No dependencies, so a
  good diagram never needs an install.
- `Shieldsmith.Mermaid` (net10.0-windows): real Mermaid, rendered by a bundled `mermaid.min.js` in a
  headless WebView2 on its own STA thread. Three traps are already solved here and must not be
  reintroduced: the page must be served from the same virtual host as the script or the browser
  blocks it cross-origin; `ExecuteScriptAsync` JSON-encodes whatever the script returns, so a
  string result must be returned as-is to leave exactly one layer to peel; and the SVG must be
  pinned to its `viewBox` before measuring, because removing its inline style re-expands it.
  PNGs come from DevTools `Page.captureScreenshot`, not from the viewport.
- `Shieldsmith.Mcp`: stdio MCP server on the official `ModelContextProtocol` SDK (pinned `[2.2.0]`).
  Its `McpException` comes from the SDK, not from us: the message reaches the model. Tools that
  can return a lot (`get_canvas_app`, `get_security_role`, desktop flow steps) are paged or
  capped; `get_canvas_app` returns the overview unless a screen is named.
- `Shieldsmith.Cli`: `shieldsmith generate|diagram|export-pack|analyze|json|erd|vsdx|word|validate-docx|check`.
- `Shieldsmith.App`: WPF shell over the same pipeline, plus AI settings and the consent dialogue.
  The palette, type and every control template live in `Theme/Brand.xaml` and come from
  cameronshields.co.uk: #00AAAA primary, #008B8B dark, #0F172A text, #F0FAFA/#E8F7F7/#C8EEEE
  tints, #E2E8F0 borders, #F8FAFC canvas, Inter with a Segoe UI fallback chain. Do not hardcode
  a colour in a view; add it to the dictionary.

The brand assets live in `src/Shieldsmith.App/Assets/` and each has one job: the horizontal
lockup is the in-app header, the stacked lockup is the About window, the square icon is the
empty state, and the `.ico` is the window and executable icon. The two lockups are trimmed to
their ink, because the supplied artwork carried enough transparent padding to render the
wordmark illegible at header size. WPF does not include images by default, so anything new has
to be declared as a `Resource` in the csproj or its pack URI silently resolves to nothing.

`Directory.Build.props` sets the version for every project and stamps a `BuildDate` assembly
metadata attribute that the About window reads. A file timestamp would be wrong: copying the
executable rewrites it.

## Format facts learned from real exports (do not regress)

- Relationships live at `ImportExportXml/EntityRelationships/EntityRelationship`, not nested
  per entity. N:N uses `FirstEntityName`/`SecondEntityName`/`IntersectEntityName`.
- Entity detail is `Entities/Entity/EntityInfo/entity` with lowercase `attributes/attribute`.
  Display names are XML attributes: `Entity/Name/@LocalizedName`,
  `displaynames/displayname/@description`.
- `RequiredLevel` is a token: none, recommended, required, systemrequired. Never "1".
- Primary key: attribute `Type == primarykey`. Primary name: `DisplayMask` contains PrimaryName.
- RootComponents carry either `schemaName` or `id` depending on type.
- Managed exports include system entities with partial metadata (Contact without its PK) and
  entities with no `EntityInfo` at all (ribbon-only changes). Both are normal, not errors.
- Env var definitions live in `environmentvariabledefinitions/<name>/…definition.xml`; values
  in `environmentvariablevalues.xml` files (root folder or per-definition folder).
- Cloud flow definitions are the `Workflows/*.json` clientdata files referenced by
  `Workflows/Workflow/JsonFileName`. Nesting lives under `actions`, `else`, `cases` and
  `default`; sibling order comes from `runAfter`, not document order. `Category` decides the
  process kind (0 classic workflow, 2 business rule, 4 BPF, 5 cloud flow).
- Forms and views are per entity: `Entity/FormXml/forms[@type]/systemform/form` and
  `Entity/SavedQueries/savedqueries/savedquery`, whose `layoutxml` is XML embedded as text.
- Canvas apps: `.msapp` entry names use backslashes in some exports and forward slashes in
  others, so normalise every name. The `.msapp` filename carries a disambiguator that cannot be
  reconstructed from the schema name, so match files by longest common prefix. `Controls/1.json`
  is the App object, not a screen, and its OnStart is where most global variables live, so read
  it for formulas but keep it out of the screen list. Modern containers put layout formulas in
  `DynamicProperties[].Rule` rather than `Rules[]`; read both and keep only
  `RuleProviderType == "User"`. Most data sources are not tables: keep `NativeCDSDataSourceInfo`.
- Copilot Studio agents are not declared in `solution.xml` at all. Find them by the
  `bots/<name>/bot.xml` folder and read behaviour from `botcomponents/*/data`, which is YAML in
  a file literally named `data`, branching on its `kind`. `activity.text` is a sequence of
  message variations, not a scalar.
- Desktop flows are workflows with `Category` 6. Two storage formats are both real: the Robin
  script inline in `Definition`, where line breaks are literal backslash-r backslash-n pairs,
  and an older base64 zip in the sidecar JSON whose `script.robin` entry is UTF-16.

## Build and verify

```
dotnet build Shieldsmith.slnx && dotnet test Shieldsmith.slnx
dotnet run --project src/Shieldsmith.Cli -- analyze data/ContosoTravel_sample.zip
pwsh build/publish.ps1        # self-contained win-x64 exes into artifacts/publish
```

Environment quirks that have already cost time:

- **Publish must run single-threaded** (`-m:1`, as `build/publish.ps1` does). Parallel file
  copies race on the SSD and fail with MSB3026/MSB4018.
- **`$PSScriptRoot` is empty inside a PowerShell 5.1 param block.** Resolve script paths in the
  body via `$MyInvocation.MyCommand.Path`.
- A running `shieldsmith.exe` locks its own output; kill background runs before rebuilding.
- Git needs `safe.directory` for this path: the SSD's file system records no ownership.

Verifying the two surfaces that unit tests cannot judge:

```
Shieldsmith.exe data/ContosoTravel_sample.zip --shot <dir>/ui.png   # a PNG per tab, per diagram,
                                                                   # plus About and consent
Shieldsmith.exe --shot <dir>/empty.png                              # the empty state
```

Then look at the PNGs. Every interface defect found so far (clipped columns, a diagram fitted to
a postage stamp, model-driven values under canvas-only headings, stale environment pills) was
caught this way and none of them failed the build. Do the same for diagrams: open the rendered
image rather than trusting that the renderer returned success.

Verification that cannot be automated here:

- **VSDX**: structural tests pass, but Visio is not installed on the dev machine. After changing
  `VsdxPackageWriter`, open a generated `.vsdx` in real Visio once before shipping.
- **AI providers**: the Claude Code CLI path has been exercised end to end against a real
  install; the Anthropic and OpenAI key paths are unit-tested with a fake provider only, so run
  one real request against each before a release.
